using System.Text;
using System.Text.RegularExpressions;
using ManfraTorrent.Core.Models;

namespace ManfraTorrent.Core.Services;

/// <summary>Resultado da filtragem: itens que sobraram e quantos foram agrupados ou escondidos.</summary>
public sealed record FilterOutcome(List<SearchResult> Items, int Duplicates, int Hidden);

/// <summary>
/// Limpa os resultados do Descobrir antes de mostrar:
/// 1. agrupa repetidos (mesmo torrent vindo de sites diferentes), ficando com o de mais seeders;
/// 2. esconde vídeos pequenos demais, gravações de cinema (CAM, TS, DVDScr...) e resoluções abaixo de 1080p,
///    conforme as opções do usuário.
/// A ordem original (relevância) é preservada.
/// </summary>
public static partial class ResultFilter
{
    // Termos de gravação de cinema / cópias de baixa qualidade, comparados como palavras inteiras do título.
    static readonly HashSet<string> LowQualityTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "CAM", "CAMRIP", "HDCAM", "TS", "HDTS", "TELESYNC", "PDVD", "PREDVD",
        "TC", "HDTC", "TELECINE", "DVDSCR", "DVDSCREENER", "SCR", "SCREENER", "WORKPRINT",
    };

    // Marcas de resolução abaixo de 1080p.
    static readonly HashSet<string> LowResTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "240P", "360P", "480P", "576P", "720P", "DVDRIP", "XVID", "DIVX",
    };

    // Marcas de 1080p ou mais: se aparecerem, o título nunca é considerado de baixa resolução.
    static readonly HashSet<string> HighResTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "1080P", "1080I", "2160P", "4K", "UHD",
    };

    public static FilterOutcome Apply(IEnumerable<SearchResult> input, AppSettings settings)
    {
        var minBytes = Math.Max(0, settings.MinVideoSizeMB) * 1024L * 1024L;
        var kept = new List<SearchResult>();
        var hidden = 0;

        foreach (var item in input)
        {
            if (ShouldHide(item, settings, minBytes)) hidden++;
            else kept.Add(item);
        }

        var deduped = Deduplicate(kept, out var duplicates);
        return new FilterOutcome(deduped, duplicates, hidden);
    }

    static bool IsVideo(SearchResult r) => r.Category is Category.Movies or Category.Series or Category.All;

    static bool ShouldHide(SearchResult r, AppSettings s, long minBytes)
    {
        if (!IsVideo(r)) return false;

        // Tamanho mínimo só para categorias de vídeo conhecidas; tamanho desconhecido passa.
        if (minBytes > 0 && r.Category is Category.Movies or Category.Series
            && r.SizeBytes is { } size && size < minBytes)
            return true;

        if (!s.HideLowQuality && !s.HideBelow1080p) return false;

        var tokens = Tokens(r.Title);
        if (s.HideLowQuality && tokens.Overlaps(LowQualityTokens)) return true;
        if (s.HideBelow1080p && tokens.Overlaps(LowResTokens) && !tokens.Overlaps(HighResTokens)) return true;
        return false;
    }

    static HashSet<string> Tokens(string title) =>
        new(TokenSplit().Split(title).Where(t => t.Length > 0), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Duas passadas: pelo infohash (o mesmo torrent em sites diferentes) e por título normalizado + tamanho
    /// (o mesmo lançamento publicado várias vezes). Fica o de mais seeders, na posição do primeiro.
    /// </summary>
    static List<SearchResult> Deduplicate(List<SearchResult> items, out int duplicates)
    {
        var byHash = Merge(items, r => HashOf(r));
        var byTitle = Merge(byHash, r => NormalizedTitle(r.Title) + "|" + ((r.SizeBytes ?? -1) / (1024 * 1024)));
        duplicates = items.Count - byTitle.Count;
        return byTitle;
    }

    static List<SearchResult> Merge(List<SearchResult> items, Func<SearchResult, string?> keyOf)
    {
        var result = new List<SearchResult>(items.Count);
        var index = new Dictionary<string, int>();
        foreach (var item in items)
        {
            var key = keyOf(item);
            if (key is null)
            {
                result.Add(item);
                continue;
            }
            if (index.TryGetValue(key, out var pos))
            {
                if ((item.Seeders ?? -1) > (result[pos].Seeders ?? -1))
                    result[pos] = item;
                continue;
            }
            index[key] = result.Count;
            result.Add(item);
        }
        return result;
    }

    static string? HashOf(SearchResult r)
    {
        if (!string.IsNullOrWhiteSpace(r.InfoHash)) return r.InfoHash.Trim().ToUpperInvariant();
        if (r.MagnetUri is { } magnet && BtihRegex().Match(magnet) is { Success: true } m)
            return m.Groups[1].Value.ToUpperInvariant();
        return null;
    }

    static string NormalizedTitle(string title)
    {
        var sb = new StringBuilder(title.Length);
        var space = false;
        foreach (var c in title.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c))
            {
                sb.Append(c);
                space = false;
            }
            else if (!space && sb.Length > 0)
            {
                sb.Append(' ');
                space = true;
            }
        }
        return sb.ToString().TrimEnd();
    }

    [GeneratedRegex(@"[^A-Za-z0-9]+")]
    private static partial Regex TokenSplit();

    [GeneratedRegex(@"xt=urn:btih:([A-Za-z0-9]+)", RegexOptions.IgnoreCase)]
    private static partial Regex BtihRegex();
}
