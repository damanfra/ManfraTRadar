using System.Text.Json;
using System.Text.Json.Serialization;
using ManfraTorrent.Core.Models;

namespace ManfraTorrent.Core.Services;

/// <summary>
/// Guarda as configurações em JSON na pasta de dados do usuário:
/// Windows: %APPDATA%\ManfraTRadar — Linux: ~/.config/ManfraTRadar
/// </summary>
public sealed class SettingsStore
{
    static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    readonly object gate = new();

    public string DataDirectory { get; }
    public string SettingsPath => Path.Combine(DataDirectory, "settings.json");
    public AppSettings Settings { get; private set; }

    SettingsStore(string dataDirectory, AppSettings settings)
    {
        DataDirectory = dataDirectory;
        Settings = settings;
    }

    public static SettingsStore Load()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ManfraTRadar");
        Directory.CreateDirectory(dir);

        var path = Path.Combine(dir, "settings.json");
        AppSettings? settings = null;
        if (File.Exists(path))
        {
            try
            {
                settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), JsonOptions);
            }
            catch (JsonException)
            {
                // Arquivo corrompido: guarda uma cópia e recomeça com o padrão.
                File.Copy(path, path + ".bak", overwrite: true);
            }
        }

        settings ??= AppSettings.CreateDefault();
        if (!settings.Sources.Any(s => s.Type == SourceType.InternetArchive))
            settings.Sources.Insert(0, new SourceConfig { Name = "Internet Archive", Type = SourceType.InternetArchive });

        var store = new SettingsStore(dir, settings);
        store.Save();
        return store;
    }

    public void Save()
    {
        lock (gate)
        {
            var tmp = SettingsPath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(Settings, JsonOptions));
            File.Move(tmp, SettingsPath, overwrite: true);
        }
    }
}
