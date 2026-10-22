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
    /// Trackers públicos somados a todo torrent (exceto privados). Lista "best" do ngosang/trackerslist
    /// em 22/10/2026; troque à vontade no settings.json.
    /// </summary>
    public List<string> GlobalTrackers { get; set; } =
    [
        "udp://tracker.opentrackr.org:1337/announce",
        "udp://open.stealth.si:80/announce",
        "udp://tracker.torrent.eu.org:451/announce",
        "udp://open.demonii.com:1337/announce",
        "udp://tracker.skynetcloud.site:6969/announce",
        "udp://tracker.qu.ax:6969/announce",
        "udp://tracker.corpscorp.online:80/announce",
        "udp://tracker.bittor.pw:1337/announce",
        "udp://tracker.ducks.party:1984/announce",
        "udp://tracker-udp.gbitt.info:80/announce",
        "udp://tracker.nyaa.vc:6969/announce",
        "udp://retracker01-msk-virt.corbina.net:80/announce",
        "udp://tracker.gmi.gd:6969/announce",
        "udp://explodie.org:6969/announce",
        "http://tracker.dler.org:6969/announce",
        "udp://tracker2.dler.org:80/announce",
        "http://tracker.dler.com:6969/announce",
        "http://tracker.renfei.net:8080/announce",
        "udp://tracker.peerfect.org:6969/announce",
        "udp://tracker.opentrackr.com:6969/announce",
    ];

    // ---- Filtros do Descobrir ----

    /// <summary>Esconde filmes e séries menores que isto (MB). 0 = desligado.</summary>
    public int MinVideoSizeMB { get; set; } = 400;

    /// <summary>Esconde gravações de cinema e cópias de baixa qualidade (CAM, TS, TC, DVDScr...).</summary>
    public bool HideLowQuality { get; set; } = true;

    /// <summary>Esconde títulos marcados com resolução abaixo de 1080p (720p, 480p, DVDRip, XviD...).</summary>
    public bool HideBelow1080p { get; set; } = true;

    // ---- Legendas (sob demanda) ----

    /// <summary>Chave de API do opensubtitles.com (criada em opensubtitles.com/consumers).</summary>
    public string OpenSubtitlesApiKey { get; set; } = "";

    /// <summary>Login do opensubtitles.com. Sem login a cota diária é menor.</summary>
    public string OpenSubtitlesUsername { get; set; } = "";
    public string OpenSubtitlesPassword { get; set; } = "";

    /// <summary>Chave de API do SubDL (subdl.com/panel/api).</summary>
    public string SubDlApiKey { get; set; } = "";

    /// <summary>Terminação do arquivo gravado: "" (mesmo nome do vídeo), ".pt-BR", ".pt" ou ".por" (+ ".srt").</summary>
    public string SubtitleSuffix { get; set; } = "";

    /// <summary>Também aceitar português de Portugal quando não houver PT-BR.</summary>
    public bool SubtitleAcceptPtPt { get; set; }

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
