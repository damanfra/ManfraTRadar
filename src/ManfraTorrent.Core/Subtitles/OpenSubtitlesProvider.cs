using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ManfraTorrent.Core.Services;

namespace ManfraTorrent.Core.Subtitles;

/// <summary>
/// API REST do opensubtitles.com (o .org antigo foi aposentado).
/// - Busca (GET /subtitles) NÃO gasta cota; download (POST /download) gasta 1.
/// - Parâmetros da busca em ordem alfabética e minúsculos (senão a API redireciona).
/// - Login guardado e reaproveitado por 12 h (o próprio login é limitado).
/// </summary>
public sealed class OpenSubtitlesProvider(SubtitleService service, string apiKey, string username, string password,
    string userAgent) : ISubtitleProvider
{
    const string DefaultHost = "api.opensubtitles.com";
    static readonly TimeSpan TokenLifetime = TimeSpan.FromHours(12);

    public string Name => "OpenSubtitles";

    public string QuotaNote
    {
        get
        {
            var st = service.State;
            return st.OsRemaining is { } r && st.OsResetUtc is { } reset && reset > DateTimeOffset.UtcNow
                ? $"usa 1 da cota do OpenSubtitles ({r} restante(s) hoje)"
                : "usa 1 da cota do OpenSubtitles";
        }
    }

    bool HasLogin => !string.IsNullOrWhiteSpace(username) && !string.IsNullOrWhiteSpace(password);

    string Host => string.IsNullOrWhiteSpace(service.State.OsBaseUrl) ? DefaultHost : service.State.OsBaseUrl!;

    public async Task<List<SubtitleCandidate>> SearchAsync(VideoInfo video, bool acceptPtPt, CancellationToken ct)
    {
        var p = video.Parsed;
        var languages = acceptPtPt ? "pt-br,pt-pt" : "pt-br";

        var query = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["ai_translated"] = "exclude",
            ["languages"] = languages,
            ["query"] = p.Title.ToLowerInvariant(),
            ["type"] = p.IsEpisode ? "episode" : "movie",
        };
        if (video.OsHash is { } hash) query["moviehash"] = hash;
        if (p.IsEpisode)
        {
            query["season_number"] = p.Season!.Value.ToString(CultureInfo.InvariantCulture);
            query["episode_number"] = p.Episode!.Value.ToString(CultureInfo.InvariantCulture);
        }
        else if (p.Year is { } year)
        {
            query["year"] = year.ToString(CultureInfo.InvariantCulture);
        }

        // Espaço como "+", como a própria API usa (evita redirecionamento).
        var qs = string.Join("&", query.Select(kv => $"{kv.Key}={Uri.EscapeDataString(kv.Value).Replace("%20", "+")}"));
        using var req = NewRequest(HttpMethod.Get, $"https://{Host}/api/v1/subtitles?{qs}", auth: false);
        using var res = await Http.Api.SendAsync(req, ct);
        await ThrowIfErrorAsync(res, ct);

        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
        var list = new List<SubtitleCandidate>();
        if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            return list;

        foreach (var item in data.EnumerateArray())
        {
            if (!item.TryGetProperty("attributes", out var a)) continue;
            if (Bool(a, "machine_translated") || Bool(a, "ai_translated")) continue;
            if (!a.TryGetProperty("files", out var files) || files.GetArrayLength() == 0) continue;

            var fileId = files[0].TryGetProperty("file_id", out var fid) ? fid.GetRawText() : null;
            if (fileId is null) continue;

            int? season = null, episode = null;
            var featureTitle = "";
            if (a.TryGetProperty("feature_details", out var fd))
            {
                season = Int(fd, "season_number");
                episode = Int(fd, "episode_number");
                featureTitle = Str(fd, "parent_title") ?? Str(fd, "title") ?? Str(fd, "movie_name") ?? "";
            }

            list.Add(new SubtitleCandidate
            {
                Provider = Name,
                Id = fileId,
                Release = Str(a, "release") ?? Str(files[0], "file_name") ?? "",
                Language = Str(a, "language") ?? "",
                Downloads = Int(a, "download_count") ?? 0,
                Uploader = a.TryGetProperty("uploader", out var up) ? Str(up, "name") ?? "" : "",
                HashMatch = Bool(a, "moviehash_match"),
                HearingImpaired = Bool(a, "hearing_impaired"),
                FromTrusted = Bool(a, "from_trusted"),
                Season = season,
                Episode = episode,
                PageUrl = Str(a, "url"),
                FeatureTitle = featureTitle,
            });
        }
        return list;
    }

    public async Task<byte[]> DownloadAsync(SubtitleCandidate candidate, VideoInfo video, CancellationToken ct)
    {
        var st = service.State;
        if (st.OsRemaining is 0 && st.OsResetUtc is { } reset && reset > DateTimeOffset.UtcNow)
            throw new SubtitleQuotaException(
                $"Cota diária do OpenSubtitles esgotada. Libera de novo às {reset.ToLocalTime():HH:mm}.", reset);

        if (HasLogin) await EnsureLoginAsync(ct);

        var link = await RequestDownloadLinkAsync(candidate, ct, allowRelogin: true);

        // O link final é um arquivo comum (não gasta cota de novo e não precisa de chave).
        return await Http.Api.GetByteArrayAsync(link, ct);
    }

    async Task<string> RequestDownloadLinkAsync(SubtitleCandidate candidate, CancellationToken ct, bool allowRelogin)
    {
        using var req = NewRequest(HttpMethod.Post, $"https://{Host}/api/v1/download", auth: true);
        req.Content = new StringContent(
            JsonSerializer.Serialize(new { file_id = long.Parse(candidate.Id, CultureInfo.InvariantCulture), sub_format = "srt" }),
            Encoding.UTF8, "application/json");

        using var res = await Http.Api.SendAsync(req, ct);
        var body = await res.Content.ReadAsStringAsync(ct);

        if (res.StatusCode == HttpStatusCode.Unauthorized && allowRelogin && HasLogin)
        {
            service.ClearOpenSubtitlesLogin();
            await EnsureLoginAsync(ct);
            return await RequestDownloadLinkAsync(candidate, ct, allowRelogin: false);
        }

        // Atualiza o contador de cota com o que a API informar (vem no 200 e no 406).
        UpdateQuota(body);

        if ((int) res.StatusCode == 406)
        {
            var reset = service.State.OsResetUtc;
            throw new SubtitleQuotaException(
                "Cota diária do OpenSubtitles esgotada" +
                (reset is { } r ? $". Libera de novo às {r.ToLocalTime():HH:mm}." : "."), reset);
        }
        if (res.StatusCode == HttpStatusCode.Unauthorized)
            throw new InvalidOperationException(HasLogin
                ? "OpenSubtitles recusou o login. Confira usuário e senha em Fontes e ajustes."
                : "OpenSubtitles pediu login para baixar. Preencha usuário e senha em Fontes e ajustes.");
        await ThrowIfErrorAsync(res, ct, body);

        using var doc = JsonDocument.Parse(body);
        return Str(doc.RootElement, "link")
               ?? throw new InvalidOperationException("OpenSubtitles não devolveu o link da legenda.");
    }

    void UpdateQuota(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            var remaining = Int(root, "remaining");
            var resetUtc = Str(root, "reset_time_utc");
            if (remaining is null && resetUtc is null) return;

            service.State.OsRemaining = remaining ?? service.State.OsRemaining;
            if (DateTimeOffset.TryParse(resetUtc, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var reset))
                service.State.OsResetUtc = reset;
            service.Save();
        }
        catch (JsonException)
        {
            // corpo sem JSON: nada a atualizar
        }
    }

    async Task EnsureLoginAsync(CancellationToken ct)
    {
        var st = service.State;
        var valid = !string.IsNullOrEmpty(st.OsToken)
                    && string.Equals(st.OsTokenUser, username, StringComparison.OrdinalIgnoreCase)
                    && st.OsTokenAt is { } at && DateTimeOffset.UtcNow - at < TokenLifetime;
        if (valid) return;

        using var req = NewRequest(HttpMethod.Post, $"https://{DefaultHost}/api/v1/login", auth: false);
        req.Content = new StringContent(JsonSerializer.Serialize(new { username, password }), Encoding.UTF8,
            "application/json");
        using var res = await Http.Api.SendAsync(req, ct);
        var body = await res.Content.ReadAsStringAsync(ct);

        if (res.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.BadRequest)
            throw new InvalidOperationException("OpenSubtitles recusou o login. Confira usuário e senha em Fontes e ajustes.");
        await ThrowIfErrorAsync(res, ct, body);

        using var doc = JsonDocument.Parse(body);
        st.OsToken = Str(doc.RootElement, "token") ?? throw new InvalidOperationException("Login sem token.");
        st.OsBaseUrl = Str(doc.RootElement, "base_url") ?? DefaultHost;
        st.OsTokenUser = username;
        st.OsTokenAt = DateTimeOffset.UtcNow;
        if (doc.RootElement.TryGetProperty("user", out var user) && Int(user, "remaining_downloads") is { } rem)
            st.OsRemaining = rem;
        service.Save();
    }

    HttpRequestMessage NewRequest(HttpMethod method, string url, bool auth)
    {
        var req = new HttpRequestMessage(method, url);
        req.Headers.TryAddWithoutValidation("Api-Key", apiKey);
        req.Headers.UserAgent.Clear();
        req.Headers.TryAddWithoutValidation("User-Agent", userAgent);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (auth && !string.IsNullOrEmpty(service.State.OsToken) && HasLogin)
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", service.State.OsToken);
        return req;
    }

    static async Task ThrowIfErrorAsync(HttpResponseMessage res, CancellationToken ct, string? body = null)
    {
        if (res.IsSuccessStatusCode) return;
        body ??= await res.Content.ReadAsStringAsync(ct);

        if (res.StatusCode == HttpStatusCode.TooManyRequests)
            throw new SubtitleQuotaException("OpenSubtitles: muitas requisições seguidas. Tente de novo em alguns segundos.");
        if (res.StatusCode == HttpStatusCode.Forbidden)
            throw new InvalidOperationException("OpenSubtitles recusou a chave de API. Confira em Fontes e ajustes.");

        string? message = null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            message = Str(doc.RootElement, "message") ?? Str(doc.RootElement, "errors");
        }
        catch (JsonException)
        {
        }
        throw new InvalidOperationException($"OpenSubtitles: {(int) res.StatusCode} {message ?? res.ReasonPhrase}");
    }

    static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v)
            ? v.ValueKind switch
            {
                JsonValueKind.String => v.GetString(),
                JsonValueKind.Number => v.GetRawText(),
                JsonValueKind.Array => string.Join("; ", v.EnumerateArray().Select(x => x.ToString())),
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

    static bool Bool(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
}
