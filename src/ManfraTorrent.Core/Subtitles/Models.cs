namespace ManfraTorrent.Core.Subtitles;

/// <summary>O que dá para tirar do nome do arquivo de vídeo.</summary>
public sealed record ParsedName(string Title, int? Year, int? Season, int? Episode, string? ReleaseGroup)
{
    public bool IsEpisode => Season is not null && Episode is not null;
}

/// <summary>Um vídeo concluído, já inspecionado localmente (sem internet).</summary>
public sealed class VideoInfo
{
    public required string Path { get; init; }
    public required long Size { get; init; }
    public required ParsedName Parsed { get; init; }

    /// <summary>Hash do OpenSubtitles (16 caracteres hex) ou nulo se não deu para ler o arquivo.</summary>
    public string? OsHash { get; init; }

    /// <summary>O que já foi encontrado no disco: .srt ao lado, faixas embutidas no MKV.</summary>
    public List<string> LocalFindings { get; init; } = [];

    /// <summary>Já existe legenda em português (arquivo ao lado ou faixa embutida).</summary>
    public bool HasPortugueseLocally { get; init; }

    public string FileName => System.IO.Path.GetFileName(Path);

    /// <summary>Chave do cache: hash + tamanho (o mesmo arquivo em outra pasta reaproveita a busca).</summary>
    public string CacheKey => $"{OsHash ?? "nohash"}:{Size}";
}

/// <summary>Uma legenda encontrada por algum serviço. Serializável (vai para o cache).</summary>
public sealed class SubtitleCandidate
{
    public string Provider { get; set; } = "";

    /// <summary>OpenSubtitles: file_id. SubDL: caminho do .zip em dl.subdl.com.</summary>
    public string Id { get; set; } = "";

    public string Release { get; set; } = "";
    public List<string> OtherReleases { get; set; } = [];
    public string Language { get; set; } = "";
    public int Downloads { get; set; }
    public string Uploader { get; set; } = "";
    public bool HashMatch { get; set; }
    public bool HearingImpaired { get; set; }
    public bool FromTrusted { get; set; }
    public int? Season { get; set; }
    public int? Episode { get; set; }
    public bool FullSeason { get; set; }
    public string? PageUrl { get; set; }

    /// <summary>Calculado pelo <see cref="SubtitleRanker"/> para ordenar.</summary>
    public double Score { get; set; }
    public bool SameGroup { get; set; }
}

public sealed class CachedSearch
{
    public DateTimeOffset SearchedAt { get; set; }
    public List<SubtitleCandidate> Candidates { get; set; } = [];
    public List<string> Errors { get; set; } = [];
}

/// <summary>Estado persistido em subtitles.json na pasta de dados.</summary>
public sealed class SubtitleState
{
    public string? OsToken { get; set; }
    public string? OsBaseUrl { get; set; }
    public string? OsTokenUser { get; set; }
    public DateTimeOffset? OsTokenAt { get; set; }

    /// <summary>Downloads restantes hoje no OpenSubtitles, como informado no último download.</summary>
    public int? OsRemaining { get; set; }
    public DateTimeOffset? OsResetUtc { get; set; }

    public Dictionary<string, CachedSearch> Cache { get; set; } = new();
}

/// <summary>Cota diária esgotada (ou bloqueio de taxa) — a mensagem já vem pronta para o usuário.</summary>
public sealed class SubtitleQuotaException(string message, DateTimeOffset? resetUtc = null) : Exception(message)
{
    public DateTimeOffset? ResetUtc { get; } = resetUtc;
}
