using System.IO.Compression;
using System.Net;
using System.Text.Json;
using ManfraTorrent.Core.Services;

namespace ManfraTorrent.Core.Subtitles;

/// <summary>
/// API do SubDL (api.subdl.com). Busca por nome; o download é um .zip em dl.subdl.com
/// (às vezes com a temporada inteira). Também tem limite diário, mas o valor não é publicado.
/// </summary>
public sealed class SubDlProvider(string apiKey, string userAgent) : ISubtitleProvider
{
    public string Name => "SubDL";

    public string QuotaNote => "SubDL (limite diário próprio, sem gastar a cota do OpenSubtitles)";

    public async Task<List<SubtitleCandidate>> SearchAsync(VideoInfo video, bool acceptPtPt, CancellationToken ct)
    {
        var p = video.Parsed;
        var query = new List<(string Key, string Value)>
        {
            ("api_key", apiKey),
            ("film_name", p.Title),
            ("languages", acceptPtPt ? "BR_PT,PT" : "BR_PT"),
            ("releases", "1"),
            ("subs_per_page", "30"),
            ("type", p.IsEpisode ? "tv" : "movie"),
        };
        if (p.IsEpisode)
        {
            query.Add(("season_number", p.Season!.Value.ToString()));
            query.Add(("episode_number", p.Episode!.Value.ToString()));
        }

        var url = "https://api.subdl.com/api/v1/subtitles?" +
                  string.Join("&", query.Select(q => $"{q.Key}={Uri.EscapeDataString(q.Value)}"));

        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.UserAgent.Clear();
        req.Headers.TryAddWithoutValidation("User-Agent", userAgent);
        using var res = await Http.Api.SendAsync(req, ct);
        var body = await res.Content.ReadAsStringAsync(ct);
        ThrowIfError(res, body);

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        var list = new List<SubtitleCandidate>();

        // "status": false com "can't find..." = nada encontrado (não é erro).
        if (root.TryGetProperty("status", out var status) && status.ValueKind == JsonValueKind.False)
        {
            var error = Str(root, "error") ?? "";
            if (error.Contains("can't find", StringComparison.OrdinalIgnoreCase)) return list;
            throw new InvalidOperationException("SubDL: " + (error.Length > 0 ? error : "erro desconhecido"));
        }

        if (!root.TryGetProperty("subtitles", out var subs) || subs.ValueKind != JsonValueKind.Array) return list;

        foreach (var s in subs.EnumerateArray())
        {
            if (s.TryGetProperty("ai_translated", out var ai) && ai.ValueKind == JsonValueKind.True) continue;
            var path = Str(s, "url");
            if (string.IsNullOrEmpty(path)) continue;

            var releases = new List<string>();
            if (s.TryGetProperty("releases", out var rel) && rel.ValueKind == JsonValueKind.Array)
                releases.AddRange(rel.EnumerateArray().Select(r => r.ToString()).Where(r => r.Length > 0));

            var release = Str(s, "release_name") ?? releases.FirstOrDefault() ?? Str(s, "name") ?? "";
            list.Add(new SubtitleCandidate
            {
                Provider = Name,
                Id = path,
                Release = release,
                OtherReleases = releases.Where(r => r != release).ToList(),
                Language = Str(s, "lang") ?? Str(s, "language") ?? "",
                Uploader = Str(s, "author") ?? "",
                HearingImpaired = s.TryGetProperty("hi", out var hi) && hi.ValueKind == JsonValueKind.True,
                Season = Int(s, "season"),
                Episode = Int(s, "episode"),
                FullSeason = s.TryGetProperty("full_season", out var fs) && fs.ValueKind == JsonValueKind.True,
                PageUrl = Str(s, "subtitlePage") is { Length: > 0 } page ? "https://subdl.com" + page : null,
            });
        }
        return list;
    }

    public async Task<byte[]> DownloadAsync(SubtitleCandidate candidate, VideoInfo video, CancellationToken ct)
    {
        var url = candidate.Id.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? candidate.Id
            : "https://dl.subdl.com" + (candidate.Id.StartsWith('/') ? "" : "/") + candidate.Id;

        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.UserAgent.Clear();
        req.Headers.TryAddWithoutValidation("User-Agent", userAgent);
        using var res = await Http.Api.SendAsync(req, ct);
        var bytes = await res.Content.ReadAsByteArrayAsync(ct);
        ThrowIfError(res, System.Text.Encoding.UTF8.GetString(bytes, 0, Math.Min(bytes.Length, 2048)));

        // Normalmente é um .zip; se vier o .srt direto, usa como está.
        if (bytes.Length < 4 || bytes[0] != 'P' || bytes[1] != 'K') return bytes;

        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        var srts = zip.Entries
            .Where(e => e.FullName.EndsWith(".srt", StringComparison.OrdinalIgnoreCase) && e.Length > 0)
            .ToList();
        if (srts.Count == 0) throw new InvalidOperationException("O pacote do SubDL não tem arquivo .srt.");

        var chosen = PickEntry(srts, video);
        using var stream = chosen.Open();
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms, ct);
        return ms.ToArray();
    }

    /// <summary>Num pacote com vários .srt: o do mesmo episódio; senão o de nome mais parecido com o vídeo.</summary>
    static ZipArchiveEntry PickEntry(List<ZipArchiveEntry> entries, VideoInfo video)
    {
        if (entries.Count == 1) return entries[0];

        var p = video.Parsed;
        if (p.IsEpisode)
        {
            var sameEpisode = entries.Where(e =>
            {
                var ep = ReleaseNameParser.Parse(Path.GetFileName(e.FullName));
                return ep.Season == p.Season && ep.Episode == p.Episode;
            }).ToList();
            if (sameEpisode.Count > 0) entries = sameEpisode;
            else throw new InvalidOperationException(
                $"O pacote do SubDL não tem legenda do episódio S{p.Season:00}E{p.Episode:00}.");
        }

        var videoTokens = ReleaseNameParser.Tokens(video.FileName);
        return entries
            .OrderByDescending(e => ReleaseNameParser.Tokens(e.FullName).Count(videoTokens.Contains))
            .ThenByDescending(e => e.Length)
            .First();
    }

    static void ThrowIfError(HttpResponseMessage res, string body)
    {
        if (res.IsSuccessStatusCode) return;

        if (res.StatusCode == HttpStatusCode.TooManyRequests)
        {
            if (body.Contains("daily_limit") || body.Contains("download_limit"))
                throw new SubtitleQuotaException("SubDL: limite diário de downloads atingido. Tente amanhã.");
            throw new SubtitleQuotaException("SubDL: muitas requisições seguidas. Tente de novo em alguns segundos.");
        }
        if (res.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)
            throw new InvalidOperationException("SubDL recusou a chave de API. Confira em Fontes e ajustes.");

        throw new InvalidOperationException($"SubDL: {(int) res.StatusCode} {res.ReasonPhrase}");
    }

    static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v)
            ? v.ValueKind switch
            {
                JsonValueKind.String => v.GetString(),
                JsonValueKind.Number => v.GetRawText(),
                _ => null,
            }
            : null;

    static int? Int(JsonElement e, string name)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out var v)) return null;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i)) return i;
        if (v.ValueKind == JsonValueKind.String && int.TryParse(v.GetString(), out i)) return i;
        return null;
    }
}
