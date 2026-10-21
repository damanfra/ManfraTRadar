namespace ManfraTorrent.Core.Services;

/// <summary>Resultado de baixar um link http(s): ou os bytes do .torrent, ou um magnet para o qual ele redirecionou.</summary>
public sealed record FetchedTorrent(byte[]? TorrentBytes, string? Magnet);

public static class TorrentFetcher
{
    public static async Task<FetchedTorrent> FetchAsync(string url, CancellationToken ct = default)
    {
        for (var hop = 0; hop < 8; hop++)
        {
            using var response = await Http.Raw.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            var status = (int) response.StatusCode;

            if (status is >= 300 and < 400)
            {
                var location = response.Headers.Location?.OriginalString;
                if (location is null && response.Headers.TryGetValues("Location", out var values))
                    location = values.FirstOrDefault();
                if (string.IsNullOrEmpty(location))
                    throw new InvalidOperationException("Redirecionamento sem destino.");

                if (location.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase))
                    return new FetchedTorrent(null, location);

                url = Uri.TryCreate(location, UriKind.Absolute, out var abs) && abs.Scheme.StartsWith("http")
                    ? abs.ToString()
                    : new Uri(new Uri(url), location).ToString();
                continue;
            }

            response.EnsureSuccessStatusCode();
            var bytes = await response.Content.ReadAsByteArrayAsync(ct);

            // Alguns indexadores devolvem o magnet no corpo em vez de redirecionar.
            if (bytes.Length > 7 && bytes.Length < 8192)
            {
                var text = System.Text.Encoding.UTF8.GetString(bytes).Trim();
                if (text.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase))
                    return new FetchedTorrent(null, text);
            }
            return new FetchedTorrent(bytes, null);
        }
        throw new InvalidOperationException("Redirecionamentos demais.");
    }
}
