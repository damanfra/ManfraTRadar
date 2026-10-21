using System.Net.Http.Headers;

namespace ManfraTorrent.Core.Services;

public static class Http
{
    static ProductInfoHeaderValue UserAgent => new("ManfraTRadar", "0.1");

    /// <summary>Cliente para APIs (segue redirecionamentos normalmente).</summary>
    public static HttpClient Api { get; } = Create(followRedirects: true);

    /// <summary>
    /// Cliente para baixar .torrent: não segue redirecionamentos sozinho, porque
    /// Jackett/Prowlarr costumam redirecionar para um link magnet:, que o HttpClient não segue.
    /// </summary>
    public static HttpClient Raw { get; } = Create(followRedirects: false);

    static HttpClient Create(bool followRedirects)
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = followRedirects,
            AutomaticDecompression = System.Net.DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        };
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.Add(UserAgent);
        return client;
    }
}
