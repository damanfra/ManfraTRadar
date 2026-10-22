using System.Reflection;

namespace ManfraTorrent.App.Services;

/// <summary>Nome e versão do app. A versão vem de &lt;Version&gt; em Directory.Build.props.</summary>
public static class AppInfo
{
    public const string Name = "ManfraTRadar";

    public static string Version { get; } = ReadVersion();

    /// <summary>Texto da barra de título: nome + etiqueta de versão.</summary>
    public static string WindowTitle => $"{Name}  v{Version}";

    static string ReadVersion()
    {
        var asm = Assembly.GetEntryAssembly() ?? typeof(AppInfo).Assembly;
        var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(info))
            return info.Split('+')[0]; // o SDK acrescenta "+<commit>"; mostra só a versão
        return asm.GetName().Version?.ToString(3) ?? "0.0.0";
    }
}
