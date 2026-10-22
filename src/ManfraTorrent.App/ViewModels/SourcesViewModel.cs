using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ManfraTorrent.App.Services;
using ManfraTorrent.Core.Models;
using ManfraTorrent.Core.Providers;
using ManfraTorrent.Core.Services;

namespace ManfraTorrent.App.ViewModels;

public partial class SourceItemViewModel : ViewModelBase
{
    public SourceItemViewModel(SourceConfig config)
    {
        Id = config.Id;
        Type = config.Type;
        name = config.Name;
        url = config.Url;
        apiKey = config.ApiKey;
        enabled = config.Enabled;
    }

    public Guid Id { get; }
    public SourceType Type { get; }
    public bool IsTorznab => Type == SourceType.Torznab;
    public string TypeLabel => IsTorznab ? "Torznab (Jackett / Prowlarr)" : "Integrado · domínio público";

    [ObservableProperty] private string name;
    [ObservableProperty] private string url;
    [ObservableProperty] private string apiKey;
    [ObservableProperty] private bool enabled;
    [ObservableProperty] private string testResult = "";
    [ObservableProperty] private bool isTesting;

    public SourceConfig ToConfig() => new()
    {
        Id = Id,
        Type = Type,
        Name = string.IsNullOrWhiteSpace(Name) ? "Fonte sem nome" : Name.Trim(),
        Url = Url.Trim(),
        ApiKey = ApiKey.Trim(),
        Enabled = Enabled,
    };

    [RelayCommand]
    private async Task Test()
    {
        var provider = ProviderFactory.Create(ToConfig());
        if (provider is null)
        {
            TestResult = "Preencha a URL do endpoint.";
            return;
        }

        IsTesting = true;
        TestResult = "Testando…";
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var items = await provider.GetTrendingAsync(Category.All, cts.Token);
            TestResult = items.Count > 0
                ? $"OK · {items.Count} itens. Ex.: {items[0].Title}"
                : "Conectou, mas não retornou itens.";
        }
        catch (Exception ex)
        {
            TestResult = "Falhou: " + ex.Message;
        }
        finally
        {
            IsTesting = false;
        }
    }
}

public partial class SourcesViewModel : ViewModelBase
{
    readonly SettingsStore store;
    readonly TorrentEngineService engine;
    readonly DiscoverViewModel discover;

    public SourcesViewModel(SettingsStore store, TorrentEngineService engine, DiscoverViewModel discover)
    {
        this.store = store;
        this.engine = engine;
        this.discover = discover;
        Load();
    }

    public ObservableCollection<SourceItemViewModel> Sources { get; } = [];

    [ObservableProperty] private string downloadFolder = "";
    [ObservableProperty] private string listenPort = "";
    [ObservableProperty] private string maxDownloadKBps = "";
    [ObservableProperty] private string maxUploadKBps = "";
    [ObservableProperty] private string statusText = "";
    [ObservableProperty] private string minVideoSizeMB = "";
    [ObservableProperty] private bool hideLowQuality;
    [ObservableProperty] private bool hideBelow1080p;

    // Legendas
    public IReadOnlyList<string> SubtitleSuffixes { get; } = [".pt-BR", ".pt", ".por"];
    [ObservableProperty] private string openSubtitlesApiKey = "";
    [ObservableProperty] private string openSubtitlesUsername = "";
    [ObservableProperty] private string openSubtitlesPassword = "";
    [ObservableProperty] private string subDlApiKey = "";
    [ObservableProperty] private string subtitleSuffix = ".pt-BR";
    [ObservableProperty] private bool subtitleAcceptPtPt;

    void Load()
    {
        var s = store.Settings;
        DownloadFolder = s.DownloadFolder;
        ListenPort = s.ListenPort.ToString();
        MaxDownloadKBps = s.MaxDownloadKBps.ToString();
        MaxUploadKBps = s.MaxUploadKBps.ToString();
        MinVideoSizeMB = s.MinVideoSizeMB.ToString();
        HideLowQuality = s.HideLowQuality;
        HideBelow1080p = s.HideBelow1080p;
        OpenSubtitlesApiKey = s.OpenSubtitlesApiKey;
        OpenSubtitlesUsername = s.OpenSubtitlesUsername;
        OpenSubtitlesPassword = s.OpenSubtitlesPassword;
        SubDlApiKey = s.SubDlApiKey;
        SubtitleSuffix = SubtitleSuffixes.Contains(s.SubtitleSuffix) ? s.SubtitleSuffix : ".pt-BR";
        SubtitleAcceptPtPt = s.SubtitleAcceptPtPt;
        Sources.Clear();
        foreach (var src in s.Sources) Sources.Add(new SourceItemViewModel(src));
    }

    [RelayCommand]
    private void AddTorznab() =>
        Sources.Add(new SourceItemViewModel(new SourceConfig
        {
            Name = "Novo indexador",
            Type = SourceType.Torznab,
            Url = "http://127.0.0.1:9117/api/v2.0/indexers/all/results/torznab/api",
            Enabled = true,
        }));

    [RelayCommand]
    private void RemoveSource(SourceItemViewModel? item)
    {
        if (item is { IsTorznab: true }) Sources.Remove(item);
    }

    [RelayCommand]
    private async Task BrowseFolder()
    {
        var folder = await UiServices.PickFolderAsync("Pasta de destino dos downloads");
        if (folder is not null) DownloadFolder = folder;
    }

    [RelayCommand]
    private async Task Save()
    {
        if (!int.TryParse(ListenPort, out var port) || port is < 1024 or > 65535)
        {
            StatusText = "Porta inválida (use de 1024 a 65535).";
            return;
        }
        if (!int.TryParse(MaxDownloadKBps, out var down) || down < 0
            || !int.TryParse(MaxUploadKBps, out var up) || up < 0)
        {
            StatusText = "Limites de velocidade precisam ser números (0 = sem limite).";
            return;
        }
        if (!int.TryParse(MinVideoSizeMB, out var minSize) || minSize < 0)
        {
            StatusText = "Tamanho mínimo precisa ser um número em MB (0 = sem filtro).";
            return;
        }
        if (string.IsNullOrWhiteSpace(DownloadFolder))
        {
            StatusText = "Escolha uma pasta de destino.";
            return;
        }

        var s = store.Settings;
        s.DownloadFolder = DownloadFolder.Trim();
        s.ListenPort = port;
        s.MaxDownloadKBps = down;
        s.MaxUploadKBps = up;
        s.MinVideoSizeMB = minSize;
        s.HideLowQuality = HideLowQuality;
        s.HideBelow1080p = HideBelow1080p;
        s.OpenSubtitlesApiKey = OpenSubtitlesApiKey.Trim();
        s.OpenSubtitlesUsername = OpenSubtitlesUsername.Trim();
        s.OpenSubtitlesPassword = OpenSubtitlesPassword;
        s.SubDlApiKey = SubDlApiKey.Trim();
        s.SubtitleSuffix = SubtitleSuffix;
        s.SubtitleAcceptPtPt = SubtitleAcceptPtPt;
        s.Sources = Sources.Select(x => x.ToConfig()).ToList();
        store.Save();

        try
        {
            await engine.ApplySettingsAsync();
            StatusText = "Salvo. Novos downloads vão para a pasta escolhida.";
        }
        catch (Exception ex)
        {
            StatusText = "Salvo, mas o motor não aceitou os ajustes agora: " + ex.Message;
        }

        _ = discover.RefreshAsync();
    }

    [RelayCommand]
    private void Revert()
    {
        Load();
        StatusText = "Alterações descartadas.";
    }

    [RelayCommand]
    private void OpenDataFolder() => UiServices.Open(store.DataDirectory);
}
