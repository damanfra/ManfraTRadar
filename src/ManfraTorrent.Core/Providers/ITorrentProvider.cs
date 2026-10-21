using ManfraTorrent.Core.Models;

namespace ManfraTorrent.Core.Providers;

public interface ITorrentProvider
{
    string Name { get; }

    /// <summary>O que está em alta agora na fonte (sem termo de busca).</summary>
    Task<IReadOnlyList<SearchResult>> GetTrendingAsync(Category category, CancellationToken ct);

    Task<IReadOnlyList<SearchResult>> SearchAsync(string query, Category category, CancellationToken ct);
}

public static class ProviderFactory
{
    public static IReadOnlyList<ITorrentProvider> Create(IEnumerable<SourceConfig> sources) =>
        sources.Where(s => s.Enabled).Select(s => Create(s)).OfType<ITorrentProvider>().ToList();

    public static ITorrentProvider? Create(SourceConfig source) => source.Type switch
    {
        SourceType.InternetArchive => new InternetArchiveProvider(),
        SourceType.Torznab when !string.IsNullOrWhiteSpace(source.Url) =>
            new TorznabProvider(source.Name, source.Url, source.ApiKey),
        _ => null,
    };
}
