using System.Globalization;
using System.Xml.Linq;
using ManfraTorrent.Core.Models;
using ManfraTorrent.Core.Services;

namespace ManfraTorrent.Core.Providers;

/// <summary>
/// Qualquer endpoint Torznab: Jackett (todos os indexadores de uma vez) ou um indexador do Prowlarr.
/// Você escolhe os indexadores no próprio Jackett/Prowlarr; o app só consome o resultado.
/// "Em alta" = busca vazia (feed dos mais recentes) ordenada por seeders.
/// </summary>
public sealed class TorznabProvider(string name, string apiUrl, string apiKey) : ITorrentProvider
{
    static readonly XNamespace Tz = "http://torznab.com/schemas/2015/feed";

    public string Name { get; } = name;

    static string CategoriesFor(Category category) => category switch
    {
        Category.Movies => "2000",
        Category.Series => "5000",
        Category.Software => "4000",
        Category.Games => "1000,4050",
        Category.Books => "7000",
        Category.Music => "3000",
        _ => "",
    };

    public async Task<IReadOnlyList<SearchResult>> GetTrendingAsync(Category category, CancellationToken ct)
    {
        var items = await QueryAsync("", category, ct);
        return items.OrderByDescending(i => i.Seeders ?? 0).Take(100).ToList();
    }

    public Task<IReadOnlyList<SearchResult>> SearchAsync(string query, Category category, CancellationToken ct) =>
        QueryAsync(query, category, ct);

    const string AllSegment = "/indexers/all/";

    /// <summary>
    /// Jackett: o endpoint "all" só responde quando o indexador mais lento termina.
    /// Aqui separamos em um provedor por indexador configurado, para os resultados chegarem aos poucos.
    /// Outros endpoints (ou qualquer falha ao listar) devolvem o próprio provedor.
    /// </summary>
    public async Task<IReadOnlyList<ITorrentProvider>> SplitByIndexerAsync(CancellationToken ct)
    {
        var at = apiUrl.IndexOf(AllSegment, StringComparison.OrdinalIgnoreCase);
        if (at < 0) return [this];

        try
        {
            var sep = apiUrl.Contains('?') ? "&" : "?";
            var xml = await Http.Api.GetStringAsync(
                $"{apiUrl}{sep}t=indexers&configured=true&apikey={Uri.EscapeDataString(apiKey)}", ct);
            var doc = XDocument.Parse(xml);

            var list = new List<ITorrentProvider>();
            foreach (var ix in doc.Root?.Elements("indexer") ?? [])
            {
                var id = (string?) ix.Attribute("id");
                if (string.IsNullOrEmpty(id)) continue;
                var title = (string?) ix.Element("title") ?? id;
                var url = apiUrl[..at] + $"/indexers/{Uri.EscapeDataString(id)}/" + apiUrl[(at + AllSegment.Length)..];
                list.Add(new TorznabProvider(title, url, apiKey));
            }
            return list.Count > 0 ? list : [this];
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return [this];
        }
    }

    /// <summary>Teste leve (t=caps): valida URL e chave sem rodar uma busca em todos os indexadores.</summary>
    public async Task<string> TestConnectionAsync(CancellationToken ct)
    {
        var sep = apiUrl.Contains('?') ? "&" : "?";
        var xml = await Http.Api.GetStringAsync($"{apiUrl}{sep}t=caps&apikey={Uri.EscapeDataString(apiKey)}", ct);
        var doc = XDocument.Parse(xml);

        if (doc.Root?.Name.LocalName == "error")
            throw new InvalidOperationException(
                $"{Name}: {(string?) doc.Root.Attribute("description") ?? "erro desconhecido"}");

        return (string?) doc.Root?.Element("server")?.Attribute("title") ?? "Torznab";
    }

    async Task<IReadOnlyList<SearchResult>> QueryAsync(string query, Category category, CancellationToken ct)
    {
        var sep = apiUrl.Contains('?') ? "&" : "?";
        var url = $"{apiUrl}{sep}t=search&apikey={Uri.EscapeDataString(apiKey)}&q={Uri.EscapeDataString(query)}&limit=100";
        var cats = CategoriesFor(category);
        if (cats.Length > 0) url += "&cat=" + cats;

        var xml = await Http.Api.GetStringAsync(url, ct);
        var doc = XDocument.Parse(xml);

        if (doc.Root?.Name.LocalName == "error")
            throw new InvalidOperationException(
                $"{Name}: {(string?) doc.Root.Attribute("description") ?? "erro desconhecido"}");

        var results = new List<SearchResult>();
        foreach (var item in doc.Descendants("item"))
        {
            var title = (string?) item.Element("title");
            if (string.IsNullOrWhiteSpace(title)) continue;

            var attrs = item.Elements(Tz + "attr")
                .Select(a => (Name: (string?) a.Attribute("name") ?? "", Value: (string?) a.Attribute("value") ?? ""))
                .ToList();
            string? Attr(string n) => attrs.FirstOrDefault(a => a.Name == n).Value is { Length: > 0 } v ? v : null;

            var link = (string?) item.Element("link");
            var enclosure = item.Element("enclosure");
            var enclosureUrl = (string?) enclosure?.Attribute("url");

            var seeders = Int(Attr("seeders"));
            var peers = Int(Attr("peers"));
            var leechers = Int(Attr("leechers")) ?? (peers is { } p && seeders is { } s ? Math.Max(0, p - s) : null);

            var magnet = Attr("magneturl");
            if (magnet is null && link?.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase) == true)
                magnet = link;

            var torrentUrl = new[] { enclosureUrl, link }
                .FirstOrDefault(u => u?.StartsWith("http", StringComparison.OrdinalIgnoreCase) == true);

            var comments = (string?) item.Element("comments");
            var guid = (string?) item.Element("guid");

            results.Add(new SearchResult
            {
                Title = title.Trim(),
                Source = Name,
                Category = MapCategory(attrs.Where(a => a.Name == "category").Select(a => a.Value), category),
                SizeBytes = Long((string?) item.Element("size")) ?? Long(Attr("size")) ?? Long((string?) enclosure?.Attribute("length")),
                Seeders = seeders,
                Leechers = leechers,
                Published = DateTimeOffset.TryParse((string?) item.Element("pubDate"), CultureInfo.InvariantCulture,
                    DateTimeStyles.AllowWhiteSpaces, out var d) ? d : null,
                MagnetUri = magnet,
                TorrentUrl = torrentUrl,
                InfoHash = Attr("infohash"),
                DetailsUrl = new[] { comments, guid }.FirstOrDefault(u => u?.StartsWith("http", StringComparison.OrdinalIgnoreCase) == true),
            });
        }
        return results;
    }

    static Category MapCategory(IEnumerable<string> ids, Category requested)
    {
        foreach (var raw in ids)
        {
            if (!int.TryParse(raw, out var id)) continue;
            if (id == 4050 || id is >= 1000 and < 2000) return Category.Games;
            if (id is >= 2000 and < 3000) return Category.Movies;
            if (id is >= 3000 and < 4000) return Category.Music;
            if (id is >= 4000 and < 5000) return Category.Software;
            if (id is >= 5000 and < 6000) return Category.Series;
            if (id is >= 7000 and < 8000) return Category.Books;
        }
        return requested;
    }

    static int? Int(string? s) => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;
    static long? Long(string? s) => long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;
}
