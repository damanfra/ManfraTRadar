using System.Text;

namespace ManfraTorrent.Core.Subtitles;

/// <summary>Converte a legenda para UTF-8 e grava ao lado do vídeo com a terminação escolhida.</summary>
public static class SubtitleWriter
{
    static SubtitleWriter() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public static string TargetPath(string videoPath, string suffix)
    {
        // Vazio = mesmo nome do vídeo (Filme.mkv → Filme.srt).
        suffix = (suffix ?? "").Trim();
        if (suffix.Length > 0 && !suffix.StartsWith('.')) suffix = "." + suffix;
        var dir = Path.GetDirectoryName(videoPath) ?? "";
        return Path.Combine(dir, Path.GetFileNameWithoutExtension(videoPath) + suffix + ".srt");
    }

    public static async Task<string> SaveAsync(byte[] raw, string videoPath, string suffix, CancellationToken ct)
    {
        var text = Decode(raw);
        var path = TargetPath(videoPath, suffix);
        // UTF-8 com BOM: VLC, MPC, Plex e TVs mais antigas leem os acentos certo.
        await File.WriteAllTextAsync(path, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), ct);
        return path;
    }

    /// <summary>
    /// Detecta a codificação: BOM (UTF-8/UTF-16); senão tenta UTF-8 estrito; se falhar, Windows-1252
    /// (o mais comum em legendas PT-BR antigas, que ficariam com "Ã©" no lugar de "é").
    /// </summary>
    public static string Decode(byte[] b)
    {
        if (b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF)
            return Encoding.UTF8.GetString(b, 3, b.Length - 3);
        if (b.Length >= 2 && b[0] == 0xFF && b[1] == 0xFE)
            return Encoding.Unicode.GetString(b, 2, b.Length - 2);
        if (b.Length >= 2 && b[0] == 0xFE && b[1] == 0xFF)
            return Encoding.BigEndianUnicode.GetString(b, 2, b.Length - 2);

        try
        {
            return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(b);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.GetEncoding(1252).GetString(b);
        }
    }
}
