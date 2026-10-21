namespace ManfraTorrent.Core.Models;

/// <summary>Um item vindo de qualquer fonte (Internet Archive, Torznab...).</summary>
public sealed record SearchResult
{
    public required string Title { get; init; }
    public required string Source { get; init; }
    public Category Category { get; init; }
    public long? SizeBytes { get; init; }

    /// <summary>Seeders informados pela fonte (Torznab). Nulo quando a fonte não informa.</summary>
    public int? Seeders { get; init; }
    public int? Leechers { get; init; }

    /// <summary>Popularidade alternativa quando não há dados do swarm (ex.: downloads na semana no Internet Archive).</summary>
    public long? Popularity { get; init; }

    public DateTimeOffset? Published { get; init; }
    public string? MagnetUri { get; init; }
    public string? TorrentUrl { get; init; }
    public string? InfoHash { get; init; }
    public string? DetailsUrl { get; init; }

    /// <summary>Pontuação usada para ordenar "em alta" misturando fontes diferentes.</summary>
    public long Score => Seeders ?? Popularity ?? 0;

    /// <summary>Melhor link para adicionar ao motor: magnet primeiro, depois .torrent.</summary>
    public string? BestLink => MagnetUri ?? TorrentUrl;
}
