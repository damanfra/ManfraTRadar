using System.Text.RegularExpressions;

namespace ManfraTorrent.Core.Subtitles;

/// <summary>Extrai título, ano, temporada/episódio e grupo do nome de um arquivo de release.</summary>
public static partial class ReleaseNameParser
{
    public static ParsedName Parse(string fileOrReleaseName)
    {
        var name = System.IO.Path.GetFileNameWithoutExtension(fileOrReleaseName);
        if (string.IsNullOrWhiteSpace(name)) name = fileOrReleaseName;

        int? season = null, episode = null, year = null;
        var cut = name.Length;

        var ep = EpisodeRegex().Match(name);
        if (!ep.Success) ep = EpisodeAltRegex().Match(name);
        if (ep.Success)
        {
            season = int.Parse(ep.Groups["s"].Value);
            episode = int.Parse(ep.Groups["e"].Value);
            cut = Math.Min(cut, ep.Index);
        }

        // Ano: o primeiro 19xx/20xx que não está no começo do nome (filmes como "2012" ficam como título).
        foreach (Match y in YearRegex().Matches(name))
        {
            if (y.Index == 0) continue;
            year = int.Parse(y.Value);
            cut = Math.Min(cut, y.Index);
            break;
        }

        // Sem ano nem episódio: corta na primeira marca técnica (1080p, WEB-DL...).
        if (cut == name.Length && TechRegex().Match(name) is { Success: true, Index: > 0 } tech)
            cut = tech.Index;

        var title = Clean(name[..cut]);
        if (title.Length == 0) title = Clean(name);

        string? group = null;
        if (GroupRegex().Match(name) is { Success: true } g)
            group = g.Groups["g"].Value;

        return new ParsedName(title, year, season, episode, group);
    }

    static string Clean(string s) =>
        SpacesRegex().Replace(s.Replace('.', ' ').Replace('_', ' ').Replace('[', ' ').Replace(']', ' ')
            .Replace('(', ' ').Replace(')', ' '), " ").Trim(' ', '-');

    /// <summary>Palavras do nome, em minúsculas, para comparar releases.</summary>
    public static HashSet<string> Tokens(string name) =>
        new(TokenRegex().Split(name.ToLowerInvariant()).Where(t => t.Length > 1), StringComparer.Ordinal);

    [GeneratedRegex(@"(?i)\bS(?<s>\d{1,2})[ ._-]?E(?<e>\d{1,3})")]
    private static partial Regex EpisodeRegex();

    [GeneratedRegex(@"(?i)\b(?<s>\d{1,2})x(?<e>\d{2,3})\b")]
    private static partial Regex EpisodeAltRegex();

    [GeneratedRegex(@"\b(19|20)\d{2}\b")]
    private static partial Regex YearRegex();

    [GeneratedRegex(@"(?i)\b(2160p|1080p|720p|480p|4k|web[ ._-]?dl|webrip|bluray|brrip|hdtv|dvdrip|x26[45]|h\.?26[45]|hevc)\b")]
    private static partial Regex TechRegex();

    [GeneratedRegex(@"-(?<g>[A-Za-z0-9]{2,20})(\[[^\]]*\])?$")]
    private static partial Regex GroupRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex SpacesRegex();

    [GeneratedRegex(@"[^a-z0-9]+")]
    private static partial Regex TokenRegex();
}
