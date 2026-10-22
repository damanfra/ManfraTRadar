namespace ManfraTorrent.Core.Models;

public enum SourceType
{
    InternetArchive,
    Torznab,
}

public sealed class SourceConfig
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public SourceType Type { get; set; }

    /// <summary>Para Torznab: endpoint completo terminando em /api (Jackett ou Prowlarr).</summary>
    public string Url { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public bool Enabled { get; set; } = true;
}

public sealed class AppSettings
{
    public string DownloadFolder { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "ManfraTRadar");

    public int ListenPort { get; set; } = 55123;

    /// <summary>KB/s. 0 = sem limite.</summary>
    public int MaxDownloadKBps { get; set; }

    /// <summary>KB/s. 0 = sem limite.</summary>
    public int MaxUploadKBps { get; set; }

    /// <summary>Infohashes (hex) que o usuário pausou; não são retomados ao abrir o app.</summary>
    public List<string> PausedTorrents { get; set; } = [];

    /// <summary>
    /// Nome amigável de cada download (infohash hex → título da busca). O nome interno do torrent
    /// às vezes é só um identificador, como no Internet Archive ("good-trick-animated-short").
    /// </summary>
    public Dictionary<string, string> DisplayNames { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public List<SourceConfig> Sources { get; set; } = [];

    public static AppSettings CreateDefault() => new()
    {
        Sources =
        [
            new SourceConfig { Name = "Internet Archive", Type = SourceType.InternetArchive, Enabled = true },
            new SourceConfig
            {
                Name = "Jackett (local)",
                Type = SourceType.Torznab,
                Url = "http://127.0.0.1:9117/api/v2.0/indexers/all/results/torznab/api",
                Enabled = false,
            },
            new SourceConfig
            {
                Name = "Prowlarr (local)",
                Type = SourceType.Torznab,
                Url = "http://127.0.0.1:9696/1/api",
                Enabled = false,
            },
        ],
    };
}
