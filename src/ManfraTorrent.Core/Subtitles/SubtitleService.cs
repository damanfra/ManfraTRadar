using System.Text.Json;
using ManfraTorrent.Core.Services;

namespace ManfraTorrent.Core.Subtitles;

/// <summary>
/// Orquestra a busca de legendas sob demanda: provedores configurados, cache de buscas (7 dias),
/// estado do login/cota do OpenSubtitles e gravação do arquivo. Persiste em subtitles.json.
/// </summary>
public sealed class SubtitleService
{
    public static readonly TimeSpan CacheLifetime = TimeSpan.FromDays(7);

    static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    readonly SettingsStore store;
    readonly string userAgent;
    readonly object gate = new();

    public SubtitleService(SettingsStore store, string appVersion)
    {
        this.store = store;
        userAgent = $"ManfraTRadar v{appVersion}";
        State = Load();
        PruneCache();
    }

    string StatePath => Path.Combine(store.DataDirectory, "subtitles.json");

    public SubtitleState State { get; }

    SubtitleState Load()
    {
        try
        {
            if (File.Exists(StatePath))
                return JsonSerializer.Deserialize<SubtitleState>(File.ReadAllText(StatePath), JsonOptions) ?? new();
        }
        catch (JsonException)
        {
            // arquivo corrompido: começa de novo
        }
        return new SubtitleState();
    }

    public void Save()
    {
        lock (gate)
        {
            var tmp = StatePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(State, JsonOptions));
            File.Move(tmp, StatePath, overwrite: true);
        }
    }

    void PruneCache()
    {
        var old = State.Cache.Where(kv => DateTimeOffset.UtcNow - kv.Value.SearchedAt > CacheLifetime)
            .Select(kv => kv.Key).ToList();
        foreach (var key in old) State.Cache.Remove(key);
        if (old.Count > 0) Save();
    }

    public void ClearOpenSubtitlesLogin()
    {
        State.OsToken = null;
        State.OsTokenAt = null;
        Save();
    }

    /// <summary>Provedores com chave preenchida, na ordem em que aparecem para o usuário.</summary>
    public List<ISubtitleProvider> Providers()
    {
        var s = store.Settings;
        var list = new List<ISubtitleProvider>();
        if (!string.IsNullOrWhiteSpace(s.OpenSubtitlesApiKey))
            list.Add(new OpenSubtitlesProvider(this, s.OpenSubtitlesApiKey.Trim(), s.OpenSubtitlesUsername.Trim(),
                s.OpenSubtitlesPassword, userAgent));
        if (!string.IsNullOrWhiteSpace(s.SubDlApiKey))
            list.Add(new SubDlProvider(s.SubDlApiKey.Trim(), userAgent));
        return list;
    }

    public ISubtitleProvider? ProviderFor(SubtitleCandidate c) =>
        Providers().FirstOrDefault(p => p.Name == c.Provider);

    /// <summary>Texto da cota do OpenSubtitles para mostrar antes de qualquer download.</summary>
    public string OpenSubtitlesQuotaText()
    {
        if (State.OsResetUtc is { } reset && reset <= DateTimeOffset.UtcNow)
        {
            // Já reiniciou desde a última informação.
            State.OsRemaining = null;
            State.OsResetUtc = null;
            Save();
        }

        var login = string.IsNullOrWhiteSpace(store.Settings.OpenSubtitlesUsername)
            ? "sem login: 5 por dia"
            : "conta gratuita: 20 por dia";
        return State.OsRemaining is { } r && State.OsResetUtc is { } next
            ? $"Cota OpenSubtitles hoje: {r} restante(s), reinicia às {next.ToLocalTime():HH:mm} ({login})"
            : $"Cota OpenSubtitles: {login} (o restante aparece depois do primeiro download)";
    }

    public CachedSearch? GetCached(VideoInfo video) =>
        State.Cache.TryGetValue(video.CacheKey, out var c) && DateTimeOffset.UtcNow - c.SearchedAt <= CacheLifetime
            ? c
            : null;

    /// <summary>Uma busca em cada provedor configurado. Guarda o resultado (inclusive "nada encontrado").</summary>
    public async Task<CachedSearch> SearchAsync(VideoInfo video, CancellationToken ct)
    {
        var providers = Providers();
        var accept = store.Settings.SubtitleAcceptPtPt;
        var all = new List<SubtitleCandidate>();
        var errors = new List<string>();

        foreach (var p in providers)
        {
            try
            {
                all.AddRange(await p.SearchAsync(video, accept, ct));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                errors.Add($"{p.Name}: {ex.Message}");
            }
        }

        var result = new CachedSearch
        {
            SearchedAt = DateTimeOffset.UtcNow,
            Candidates = SubtitleRanker.Rank(all, video),
            Errors = errors,
        };

        // Só guarda se ao menos um provedor respondeu (erro de rede não vira "nada encontrado").
        if (errors.Count < providers.Count)
        {
            State.Cache[video.CacheKey] = result;
            Save();
        }
        return result;
    }

    /// <summary>Baixa a legenda escolhida (1 download) e grava ao lado do vídeo. Devolve o caminho.</summary>
    public async Task<string> DownloadAndSaveAsync(VideoInfo video, SubtitleCandidate candidate, CancellationToken ct)
    {
        var provider = ProviderFor(candidate)
                       ?? throw new InvalidOperationException($"{candidate.Provider} não está configurado.");
        var raw = await provider.DownloadAsync(candidate, video, ct);
        return await SubtitleWriter.SaveAsync(raw, video.Path, store.Settings.SubtitleSuffix, ct);
    }

    public string TargetPath(VideoInfo video) => SubtitleWriter.TargetPath(video.Path, store.Settings.SubtitleSuffix);
}
