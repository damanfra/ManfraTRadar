using System.Text;

namespace ManfraTorrent.Core.Subtitles;

/// <summary>
/// Tudo o que dá para descobrir sem internet: quais arquivos são vídeos de verdade, se já existe legenda
/// em português ao lado ou embutida no MKV, e o hash do OpenSubtitles.
/// </summary>
public static class VideoInspector
{
    static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mkv", ".mp4", ".m4v", ".avi", ".mov", ".wmv", ".mpg", ".mpeg", ".webm",
    };

    static readonly HashSet<string> SubtitleExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".srt", ".ass", ".ssa", ".sub", ".vtt",
    };

    // Palavras que indicam português no nome de um arquivo de legenda.
    static readonly HashSet<string> PortugueseTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "pt", "ptbr", "pb", "por", "pob", "portuguese", "portugues", "português", "brazilian", "brazil", "brasil",
        "brasileiro",
    };

    const long MinVideoBytes = 100L * 1024 * 1024;

    /// <summary>Vídeos de um download concluído: ignora amostras e extras pequenos.</summary>
    public static List<string> FindVideos(IEnumerable<string> torrentFiles)
    {
        var videos = torrentFiles
            .Where(f => VideoExtensions.Contains(Path.GetExtension(f)) && File.Exists(f))
            .ToList();

        var real = videos
            .Where(f => !Path.GetFileName(f).Contains("sample", StringComparison.OrdinalIgnoreCase)
                        && new FileInfo(f).Length >= MinVideoBytes)
            .ToList();

        // Se só existir um vídeo pequeno, usa ele mesmo assim.
        return (real.Count > 0 ? real : videos).OrderBy(f => f, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public static VideoInfo Inspect(string videoPath, IEnumerable<string> torrentFiles)
    {
        var findings = new List<string>();
        var hasPortuguese = false;

        // 1) Legendas soltas: ao lado do vídeo e em qualquer pasta do torrent (ex.: "Subs/").
        var baseName = Path.GetFileNameWithoutExtension(videoPath);
        var videoParsed = ReleaseNameParser.Parse(Path.GetFileName(videoPath));
        var candidates = torrentFiles
            .Concat(SafeListDirectory(Path.GetDirectoryName(videoPath)))
            .Where(f => SubtitleExtensions.Contains(Path.GetExtension(f)) && File.Exists(f))
            .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var sub in candidates)
        {
            var subName = Path.GetFileNameWithoutExtension(sub);

            // Em pacotes de temporada, ignora legendas de outros episódios.
            var subParsed = ReleaseNameParser.Parse(Path.GetFileName(sub));
            if (videoParsed.IsEpisode && subParsed.IsEpisode
                && (subParsed.Season != videoParsed.Season || subParsed.Episode != videoParsed.Episode))
                continue;

            var tokens = ReleaseNameParser.Tokens(subName);
            tokens.UnionWith(ReleaseNameParser.Tokens(Path.GetFileName(Path.GetDirectoryName(sub) ?? "")));
            var isPortuguese = tokens.Overlaps(PortugueseTokens);
            var sameVideo = subName.StartsWith(baseName, StringComparison.OrdinalIgnoreCase);

            if (isPortuguese)
            {
                hasPortuguese = true;
                findings.Add($"Legenda em português já existe: {Path.GetFileName(sub)}");
            }
            else if (sameVideo)
            {
                findings.Add($"Legenda de idioma não identificado: {Path.GetFileName(sub)}");
            }
        }

        // 2) Faixas de legenda embutidas no MKV.
        if (Path.GetExtension(videoPath).Equals(".mkv", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var track in MkvReader.SafeReadSubtitleTracks(videoPath))
            {
                if (track.IsPortuguese)
                {
                    hasPortuguese = true;
                    findings.Add($"Legenda em português embutida no vídeo: {track.Describe()}");
                }
            }
        }

        var size = new FileInfo(videoPath).Length;
        return new VideoInfo
        {
            Path = videoPath,
            Size = size,
            Parsed = videoParsed,
            OsHash = SafeHash(videoPath),
            LocalFindings = findings,
            HasPortugueseLocally = hasPortuguese,
        };
    }

    static IEnumerable<string> SafeListDirectory(string? dir)
    {
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return [];
        try
        {
            return Directory.EnumerateFiles(dir).ToList();
        }
        catch
        {
            return [];
        }
    }

    static string? SafeHash(string path)
    {
        try
        {
            return OpenSubtitlesHash(path);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Hash do OpenSubtitles: tamanho do arquivo + soma (64 bits) dos primeiros e dos últimos 64 KB,
    /// lidos como inteiros little-endian. Só lê 128 KB, então é instantâneo.
    /// </summary>
    public static string OpenSubtitlesHash(string path)
    {
        const int chunk = 64 * 1024;
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var size = fs.Length;
        if (size < chunk) throw new InvalidOperationException("Arquivo pequeno demais para o hash.");

        ulong hash = (ulong) size;
        var buffer = new byte[chunk];

        ReadExactly(fs, buffer);
        hash = AddChunk(hash, buffer);

        fs.Seek(size - chunk, SeekOrigin.Begin);
        ReadExactly(fs, buffer);
        hash = AddChunk(hash, buffer);

        return hash.ToString("x16");
    }

    static void ReadExactly(Stream s, byte[] buffer)
    {
        var read = 0;
        while (read < buffer.Length)
        {
            var n = s.Read(buffer, read, buffer.Length - read);
            if (n <= 0) throw new EndOfStreamException();
            read += n;
        }
    }

    static ulong AddChunk(ulong hash, byte[] buffer)
    {
        unchecked
        {
            for (var i = 0; i < buffer.Length; i += 8)
                hash += BitConverter.ToUInt64(buffer, i);
        }
        return hash;
    }
}

/// <summary>Uma faixa de legenda de um MKV.</summary>
public sealed record MkvSubtitleTrack(string Language, string? LanguageIetf, string? Name)
{
    public bool IsPortuguese
    {
        get
        {
            var lang = (LanguageIetf ?? Language).ToLowerInvariant();
            if (lang is "por" or "pt" or "pob" or "pb" || lang.StartsWith("pt-")) return true;
            var name = Name?.ToLowerInvariant() ?? "";
            return name.Contains("portugu") || name.Contains("brasil") || name.Contains("brazil");
        }
    }

    public string Describe()
    {
        var code = LanguageIetf ?? Language;
        return string.IsNullOrWhiteSpace(Name) ? code : $"{Name} ({code})";
    }
}

/// <summary>
/// Leitor mínimo de Matroska (EBML): só percorre o começo do arquivo até achar o elemento Tracks
/// e lê o idioma/nome das faixas de legenda. Para no primeiro Cluster (os dados de vídeo).
/// </summary>
public static class MkvReader
{
    const long IdEbml = 0x1A45DFA3;
    const long IdSegment = 0x18538067;
    const long IdTracks = 0x1654AE6B;
    const long IdCluster = 0x1F43B675;
    const long IdTrackEntry = 0xAE;
    const long IdTrackType = 0x83;
    const long IdLanguage = 0x22B59C;
    const long IdLanguageIetf = 0x22B59D;
    const long IdName = 0x536E;
    const int TrackTypeSubtitle = 0x11;

    public static List<MkvSubtitleTrack> SafeReadSubtitleTracks(string path)
    {
        try
        {
            return ReadSubtitleTracks(path);
        }
        catch
        {
            return [];
        }
    }

    public static List<MkvSubtitleTrack> ReadSubtitleTracks(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096);

        if (ReadId(fs) != IdEbml) return [];
        var headerSize = ReadSize(fs);
        if (headerSize < 0) return [];
        fs.Seek(headerSize, SeekOrigin.Current);

        if (ReadId(fs) != IdSegment) return [];
        var segmentSize = ReadSize(fs);
        var segmentEnd = segmentSize < 0 ? fs.Length : Math.Min(fs.Length, fs.Position + segmentSize);

        // Percorre os filhos do Segment até achar Tracks (normalmente nos primeiros KB).
        var guard = 0;
        while (fs.Position < segmentEnd && guard++ < 1000)
        {
            var id = ReadId(fs);
            var size = ReadSize(fs);
            if (id < 0 || size < 0 || id == IdCluster) break;
            if (id == IdTracks) return ParseTracks(fs, fs.Position + size);
            fs.Seek(size, SeekOrigin.Current);
        }
        return [];
    }

    static List<MkvSubtitleTrack> ParseTracks(Stream fs, long end)
    {
        var result = new List<MkvSubtitleTrack>();
        while (fs.Position < end)
        {
            var id = ReadId(fs);
            var size = ReadSize(fs);
            if (id < 0 || size < 0) break;
            var entryEnd = fs.Position + size;

            if (id == IdTrackEntry)
            {
                int? type = null;
                string language = "eng"; // padrão do Matroska quando o elemento não existe
                string? ietf = null, name = null;

                while (fs.Position < entryEnd)
                {
                    var cid = ReadId(fs);
                    var csize = ReadSize(fs);
                    if (cid < 0 || csize < 0) break;
                    if (cid == IdTrackType) type = (int) ReadUInt(fs, (int) csize);
                    else if (cid == IdLanguage) language = ReadString(fs, (int) csize, Encoding.ASCII);
                    else if (cid == IdLanguageIetf) ietf = ReadString(fs, (int) csize, Encoding.ASCII);
                    else if (cid == IdName) name = ReadString(fs, (int) csize, Encoding.UTF8);
                    else fs.Seek(csize, SeekOrigin.Current);
                }

                if (type == TrackTypeSubtitle)
                    result.Add(new MkvSubtitleTrack(language, ietf, name));
            }

            fs.Seek(entryEnd, SeekOrigin.Begin);
        }
        return result;
    }

    /// <summary>ID de elemento EBML (mantém o bit marcador). -1 se inválido/fim.</summary>
    static long ReadId(Stream s)
    {
        var first = s.ReadByte();
        if (first <= 0) return -1;
        var length = 1;
        var mask = 0x80;
        while (length <= 4 && (first & mask) == 0)
        {
            mask >>= 1;
            length++;
        }
        if (length > 4) return -1;

        long value = first;
        for (var i = 1; i < length; i++)
        {
            var b = s.ReadByte();
            if (b < 0) return -1;
            value = (value << 8) | (uint) b;
        }
        return value;
    }

    /// <summary>Tamanho EBML (sem o bit marcador). -1 se desconhecido ou inválido.</summary>
    static long ReadSize(Stream s)
    {
        var first = s.ReadByte();
        if (first <= 0) return -1;
        var length = 1;
        var mask = 0x80;
        while (length <= 8 && (first & mask) == 0)
        {
            mask >>= 1;
            length++;
        }
        if (length > 8) return -1;

        long value = first & (mask - 1);
        var allOnes = value == mask - 1;
        for (var i = 1; i < length; i++)
        {
            var b = s.ReadByte();
            if (b < 0) return -1;
            if (b != 0xFF) allOnes = false;
            value = (value << 8) | (uint) b;
        }
        return allOnes ? -1 : value;
    }

    static ulong ReadUInt(Stream s, int size)
    {
        ulong value = 0;
        for (var i = 0; i < size; i++)
        {
            var b = s.ReadByte();
            if (b < 0) break;
            value = (value << 8) | (uint) b;
        }
        return value;
    }

    static string ReadString(Stream s, int size, Encoding encoding)
    {
        if (size is <= 0 or > 4096)
        {
            s.Seek(Math.Max(0, size), SeekOrigin.Current);
            return "";
        }
        var buffer = new byte[size];
        var read = 0;
        while (read < size)
        {
            var n = s.Read(buffer, read, size - read);
            if (n <= 0) break;
            read += n;
        }
        return encoding.GetString(buffer, 0, read).TrimEnd('\0');
    }
}
