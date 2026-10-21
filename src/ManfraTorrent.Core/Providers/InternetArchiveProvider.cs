using System.Text.Json;
using ManfraTorrent.Core.Models;
using ManfraTorrent.Core.Services;

namespace ManfraTorrent.Core.Providers;

/// <summary>
/// Internet Archive: todo item tem um .torrent em /download/{id}/{id}_archive.torrent.
/// "Em alta" = mais baixados na última semana (campo "week" da API advancedsearch).
/// </summary>
public sealed class InternetArchiveProvider : ITorrentProvider
{
    public string Name => "Internet Archive";

    static string ScopeFor(Category category) => category switch
    {
        Category.Movies => "collection:(feature_films)",
        Category.Series => "collection:(classic_tv)",
        Category.Software => "mediatype:(software) AND -collection:(softwarelibrary_msdos_games)",
        Category.Games => "collection:(softwarelibrary_msdos_games)",
        Category.Books => "collection:(gutenberg)",
        Category.Music => "collection:(etree)",
        _ => "collection:(feature_films OR classic_tv OR opensource_movies OR gutenberg OR etree)",
    };

    public Task<IReadOnlyList<SearchResult>> GetTrendingAsync(Category category, CancellationToken ct) =>
        QueryAsync(ScopeFor(category), "week desc", category, ct);

    public Task<IReadOnlyList<SearchResult>> SearchAsync(string query, Category category, CancellationToken ct)
    {
        var terms = new string(query.Where(c => !"()\"\\:".Contains(c)).ToArray()).Trim();
        return QueryAsync($"({terms}) AND {ScopeFor(category)}", "downloads desc", category, ct);
    }

    async Task<IReadOnlyList<SearchResult>> QueryAsync(string q, string sort, Category category, CancellationToken ct)
    {
        string[] fields = ["identifier", "title", "item_size", "downloads", "week", "publicdate", "mediatype", "collection"];
        var url = "https://archive.org/advancedsearch.php?q=" + Uri.EscapeDataString(q)
                  + string.Concat(fields.Select(f => "&fl%5B%5D=" + f))
                  + "&sort%5B%5D=" + Uri.EscapeDataString(sort)
                  + "&rows=60&page=1&output=json";

        await using var stream = await Http.Api.GetStreamAsync(url, ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        var results = new List<SearchResult>();
        if (!doc.RootElement.TryGetProperty("response", out var response)
            || !response.TryGetProperty("docs", out var docs))
            return results;

        foreach (var item in docs.EnumerateArray())
        {
            var id = Str(item, "identifier");
            if (string.IsNullOrEmpty(id)) continue;

            results.Add(new SearchResult
            {
                Title = Str(item, "title") ?? id,
                Source = Name,
                Category = category == Category.All ? Infer(item) : category,
                SizeBytes = Num(item, "item_size"),
                Popularity = Num(item, "week") ?? Num(item, "downloads"),
                Published = DateTimeOffset.TryParse(Str(item, "publicdate"), out var d) ? d : null,
                TorrentUrl = $"https://archive.org/download/{id}/{id}_archive.torrent",
                DetailsUrl = $"https://archive.org/details/{id}",
            });
        }
        return results;
    }

    static Category Infer(JsonElement item)
    {
        var collections = Strs(item, "collection");
        return Str(item, "mediatype") switch
        {
            "movies" when collections.Contains("classic_tv") => Category.Series,
            "movies" => Category.Movies,
            "texts" => Category.Books,
            "audio" or "etree" => Category.Music,
            "software" when collections.Contains("softwarelibrary_msdos_games") => Category.Games,
            "software" => Category.Software,
            _ => Category.All,
        };
    }

    // A API devolve alguns campos ora como valor simples, ora como array.
    static string? Str(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var v)) return null;
        return v.ValueKind switch
        {
            JsonValueKind.String => v.GetString(),
            JsonValueKind.Array when v.GetArrayLength() > 0 => v[0].ToString(),
            JsonValueKind.Number => v.GetRawText(),
            _ => null,
        };
    }

    static HashSet<string> Strs(JsonElement e, string name)
    {
        var set = new HashSet<string>();
        if (!e.TryGetProperty(name, out var v)) return set;
        if (v.ValueKind == JsonValueKind.Array)
            foreach (var x in v.EnumerateArray()) set.Add(x.ToString());
        else if (v.ValueKind == JsonValueKind.String)
            set.Add(v.GetString()!);
        return set;
    }

    static long? Num(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var v)) return null;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n)) return n;
        if (v.ValueKind == JsonValueKind.Number) return (long) v.GetDouble();
        if (v.ValueKind == JsonValueKind.String && long.TryParse(v.GetString(), out n)) return n;
        return null;
    }
}
