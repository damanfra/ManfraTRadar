namespace ManfraTorrent.Core.Subtitles;

/// <summary>
/// Ordena as legendas encontradas: primeiro as sincronizadas com o arquivo (hash do OpenSubtitles),
/// depois as do mesmo grupo de release, depois as de nome mais parecido; desempate por downloads.
/// Esconde resultados de outros títulos: a busca por nome dos serviços é "frouxa" (qualquer filme com
/// "of"/"the" no nome pode aparecer), então exige todas as palavras do termo buscado.
/// </summary>
public static class SubtitleRanker
{
    public static List<SubtitleCandidate> Rank(IEnumerable<SubtitleCandidate> candidates, VideoInfo video,
        out int hiddenOtherTitles)
    {
        hiddenOtherTitles = 0;
        var titleTokens = ReleaseNameParser.Tokens(video.Parsed.Title);
        var videoTokens = ReleaseNameParser.Tokens(video.FileName);
        var group = video.Parsed.ReleaseGroup?.ToLowerInvariant();
        var p = video.Parsed;

        var list = new List<SubtitleCandidate>();
        foreach (var c in candidates)
        {
            // Episódio errado (quando a fonte informa) não serve.
            if (p.IsEpisode && !c.FullSeason && c.Season is { } cs && c.Episode is { } ce && ce > 0
                && (cs != p.Season || ce != p.Episode))
                continue;

            var names = new[] { c.Release }.Concat(c.OtherReleases).Where(n => !string.IsNullOrWhiteSpace(n)).ToList();

            // Outro título? (a sincronizada pelo hash sempre fica, é o mesmo arquivo)
            if (!c.HashMatch && titleTokens.Count > 0
                && !names.Append(c.FeatureTitle).Any(n => titleTokens.IsSubsetOf(ReleaseNameParser.Tokens(n))))
            {
                hiddenOtherTitles++;
                continue;
            }
            var bestOverlap = 0;
            var sameGroup = false;
            foreach (var name in names)
            {
                var tokens = ReleaseNameParser.Tokens(name);
                bestOverlap = Math.Max(bestOverlap, tokens.Count(videoTokens.Contains));
                if (group is not null && tokens.Contains(group)) sameGroup = true;
            }

            c.SameGroup = sameGroup;
            c.Score = (c.HashMatch ? 1000 : 0)
                      + (sameGroup ? 200 : 0)
                      + bestOverlap * 10
                      + (c.Language.Contains("br", StringComparison.OrdinalIgnoreCase) ? 50 : 0)
                      + (c.FromTrusted ? 5 : 0)
                      - (c.HearingImpaired ? 3 : 0)
                      + Math.Log10(c.Downloads + 1);
            list.Add(c);
        }

        return list.OrderByDescending(c => c.Score).ToList();
    }
}
