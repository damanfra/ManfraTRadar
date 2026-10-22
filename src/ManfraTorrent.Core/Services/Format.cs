using System.Globalization;

namespace ManfraTorrent.Core.Services;

public static class Format
{
    static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-BR");
    static readonly string[] Units = ["B", "KB", "MB", "GB", "TB"];

    public static string Bytes(long? bytes)
    {
        if (bytes is null or < 0) return "—";
        double value = bytes.Value;
        var unit = 0;
        while (value >= 1024 && unit < Units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return unit == 0
            ? $"{value.ToString("0", Pt)} {Units[unit]}"
            : $"{value.ToString(value >= 100 ? "0" : "0.0", Pt)} {Units[unit]}";
    }

    public static string Speed(long bytesPerSecond) => Bytes(bytesPerSecond) + "/s";

    public static string Number(long value) => value.ToString("N0", Pt);

    public static string Eta(long remainingBytes, long bytesPerSecond)
    {
        if (bytesPerSecond <= 0 || remainingBytes <= 0) return "∞";
        var t = TimeSpan.FromSeconds(remainingBytes / (double) bytesPerSecond);
        if (t.TotalDays >= 1) return $"{(int) t.TotalDays} d {t.Hours} h";
        if (t.TotalHours >= 1) return $"{(int) t.TotalHours} h {t.Minutes} min";
        if (t.TotalMinutes >= 1) return $"{(int) t.TotalMinutes} min";
        return $"{t.Seconds} s";
    }

    /// <summary>Duração curta: "3 min", "2 h 10 min", "1 d 4 h".</summary>
    public static string Duration(TimeSpan t)
    {
        if (t < TimeSpan.Zero) t = TimeSpan.Zero;
        if (t.TotalDays >= 1) return $"{(int) t.TotalDays} d {t.Hours} h";
        if (t.TotalHours >= 1) return $"{(int) t.TotalHours} h {t.Minutes} min";
        return $"{Math.Max(1, (int) t.TotalMinutes)} min";
    }

    public static string Age(DateTimeOffset? when)
    {
        if (when is null) return "";
        var d = DateTimeOffset.Now - when.Value;
        if (d.TotalMinutes < 60) return $"há {Math.Max(1, (int) d.TotalMinutes)} min";
        if (d.TotalHours < 24) return $"há {(int) d.TotalHours} h";
        if (d.TotalDays < 60) return $"há {(int) d.TotalDays} d";
        if (d.TotalDays < 730) return $"há {(int) (d.TotalDays / 30)} meses";
        return when.Value.Year.ToString(Pt);
    }
}
