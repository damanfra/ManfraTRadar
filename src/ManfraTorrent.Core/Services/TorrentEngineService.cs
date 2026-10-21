using System.Net;
using MonoTorrent;
using MonoTorrent.Client;

namespace ManfraTorrent.Core.Services;

/// <summary>
/// Envolve o ClientEngine do MonoTorrent: inicia/restaura o estado, adiciona torrents
/// (magnet, URL de .torrent ou arquivo local), pausa, retoma, remove e salva tudo ao sair.
/// </summary>
public sealed class TorrentEngineService
{
    readonly SettingsStore store;
    readonly SemaphoreSlim gate = new(1, 1);

    public TorrentEngineService(SettingsStore store) => this.store = store;

    public ClientEngine? Engine { get; private set; }
    public bool IsReady => Engine is not null;

    string StatePath => Path.Combine(store.DataDirectory, "engine.state");
    string CacheDirectory => Path.Combine(store.DataDirectory, "cache");

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(CacheDirectory);

        if (File.Exists(StatePath))
        {
            try
            {
                Engine = await ClientEngine.RestoreStateAsync(StatePath);
            }
            catch
            {
                // Estado ilegível (versão antiga, arquivo truncado): começa limpo, mas guarda o antigo.
                File.Copy(StatePath, StatePath + ".bak", overwrite: true);
                Engine = null;
            }
        }

        Engine ??= new ClientEngine(BuildSettings(null));

        // Garante que limites e porta atuais valham mesmo com estado restaurado.
        await Engine.UpdateSettingsAsync(BuildSettings(Engine.Settings));

        var paused = store.Settings.PausedTorrents.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var manager in Engine.Torrents.ToList())
        {
            if (!paused.Contains(KeyOf(manager)))
                await manager.StartAsync();
        }
    }

    EngineSettings BuildSettings(EngineSettings? current)
    {
        var s = store.Settings;
        var builder = current is null ? new EngineSettingsBuilder() : new EngineSettingsBuilder(current);
        builder.CacheDirectory = CacheDirectory;
        builder.ListenEndPoints = new Dictionary<string, IPEndPoint>
        {
            ["ipv4"] = new IPEndPoint(IPAddress.Any, s.ListenPort),
        };
        builder.DhtEndPoint = new IPEndPoint(IPAddress.Any, s.ListenPort);
        builder.MaximumDownloadRate = Math.Max(0, s.MaxDownloadKBps) * 1024;
        builder.MaximumUploadRate = Math.Max(0, s.MaxUploadKBps) * 1024;
        builder.AllowPortForwarding = true;
        builder.AutoSaveLoadDhtCache = true;
        builder.AutoSaveLoadFastResume = true;
        builder.AutoSaveLoadMagnetLinkMetadata = true;
        return builder.ToSettings();
    }

    /// <summary>Reaplica porta e limites depois que o usuário salva os ajustes.</summary>
    public async Task ApplySettingsAsync()
    {
        if (Engine is null) return;
        await Engine.UpdateSettingsAsync(BuildSettings(Engine.Settings));
    }

    public IReadOnlyList<TorrentManager> Managers
    {
        get
        {
            if (Engine is null) return [];
            try
            {
                return Engine.Torrents.ToArray();
            }
            catch (InvalidOperationException)
            {
                return []; // lista alterada durante a cópia; o próximo tick resolve
            }
        }
    }

    public static string KeyOf(TorrentManager manager) => manager.InfoHashes.V1OrV2.ToHex();

    /// <summary>Aceita link magnet, URL http(s) de .torrent ou caminho de arquivo .torrent.</summary>
    public async Task<TorrentManager> AddAsync(string input, bool sequential, CancellationToken ct = default)
    {
        if (Engine is null) throw new InvalidOperationException("O motor ainda está iniciando.");
        input = input.Trim().Trim('"');

        MagnetLink? magnet = null;
        Torrent? torrent = null;

        if (input.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase))
        {
            magnet = MagnetLink.Parse(input);
        }
        else if (input.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                 || input.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            var fetched = await TorrentFetcher.FetchAsync(input, ct);
            if (fetched.Magnet is not null)
                magnet = MagnetLink.Parse(fetched.Magnet);
            else
                torrent = Torrent.Load(new MemoryStream(fetched.TorrentBytes!));
        }
        else if (File.Exists(input))
        {
            torrent = await Torrent.LoadAsync(input);
        }
        else
        {
            throw new ArgumentException("Use um link magnet, uma URL de .torrent ou um arquivo .torrent.");
        }

        var infoHashes = torrent?.InfoHashes ?? magnet!.InfoHashes;

        await gate.WaitAsync(ct);
        try
        {
            var existing = Engine.Torrents.FirstOrDefault(t => t.InfoHashes.Equals(infoHashes));
            if (existing is not null)
            {
                await ResumeAsync(existing);
                return existing;
            }

            var saveDirectory = store.Settings.DownloadFolder;
            Directory.CreateDirectory(saveDirectory);
            var settings = new TorrentSettingsBuilder { MaximumConnections = 80 }.ToSettings();

            TorrentManager manager;
            if (torrent is not null)
                manager = sequential
                    ? await Engine.AddStreamingAsync(torrent, saveDirectory, settings)
                    : await Engine.AddAsync(torrent, saveDirectory, settings);
            else
                manager = sequential
                    ? await Engine.AddStreamingAsync(magnet!, saveDirectory, settings)
                    : await Engine.AddAsync(magnet!, saveDirectory, settings);

            await manager.StartAsync();
            await SaveStateCoreAsync();
            return manager;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task PauseAsync(TorrentManager manager)
    {
        await manager.StopAsync();
        var key = KeyOf(manager);
        if (!store.Settings.PausedTorrents.Contains(key, StringComparer.OrdinalIgnoreCase))
        {
            store.Settings.PausedTorrents.Add(key);
            store.Save();
        }
    }

    public async Task ResumeAsync(TorrentManager manager)
    {
        if (manager.State is TorrentState.Stopped or TorrentState.Paused or TorrentState.Error)
            await manager.StartAsync();
        if (store.Settings.PausedTorrents.RemoveAll(k => string.Equals(k, KeyOf(manager), StringComparison.OrdinalIgnoreCase)) > 0)
            store.Save();
    }

    public async Task RemoveAsync(TorrentManager manager, bool deleteFiles)
    {
        if (Engine is null) return;
        await gate.WaitAsync();
        try
        {
            if (manager.State != TorrentState.Stopped)
                await manager.StopAsync();
            await Engine.RemoveAsync(manager,
                deleteFiles ? RemoveMode.CacheDataAndDownloadedData : RemoveMode.CacheDataOnly);
            store.Settings.PausedTorrents.RemoveAll(k => string.Equals(k, KeyOf(manager), StringComparison.OrdinalIgnoreCase));
            store.Save();
            await SaveStateCoreAsync();
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task SaveStateAsync()
    {
        await gate.WaitAsync();
        try
        {
            await SaveStateCoreAsync();
        }
        finally
        {
            gate.Release();
        }
    }

    async Task SaveStateCoreAsync()
    {
        if (Engine is null) return;
        var tmp = StatePath + ".tmp";
        await Engine.SaveStateAsync(tmp);
        File.Move(tmp, StatePath, overwrite: true);
    }

    public async Task ShutdownAsync()
    {
        if (Engine is null) return;
        try
        {
            await SaveStateAsync();
            await Engine.StopAllAsync(TimeSpan.FromSeconds(5));
            await SaveStateAsync();
        }
        finally
        {
            Engine.Dispose();
            Engine = null;
        }
    }
}
