using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ManfraTorrent.Core.Services;

namespace ManfraTorrent.App.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    readonly TorrentEngineService engine;

    public MainWindowViewModel(SettingsStore store, TorrentEngineService engine)
    {
        this.engine = engine;
        Downloads = new DownloadsViewModel(store, engine);
        Discover = new DiscoverViewModel(store, engine, Downloads);
        Sources = new SourcesViewModel(store, engine, Discover);
        currentPage = Discover;
    }

    public DiscoverViewModel Discover { get; }
    public DownloadsViewModel Downloads { get; }
    public SourcesViewModel Sources { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDiscover), nameof(IsDownloads), nameof(IsSources))]
    private ViewModelBase currentPage;

    [ObservableProperty] private string engineStatus = "Iniciando motor…";

    public bool IsDiscover => CurrentPage == Discover;
    public bool IsDownloads => CurrentPage == Downloads;
    public bool IsSources => CurrentPage == Sources;

    [RelayCommand]
    private void Navigate(string page) => CurrentPage = page switch
    {
        "downloads" => Downloads,
        "sources" => Sources,
        _ => Discover,
    };

    public async Task InitializeAsync()
    {
        var trending = Discover.RefreshAsync();
        try
        {
            await engine.InitializeAsync();
            EngineStatus = "Motor pronto";
            Downloads.Start();
        }
        catch (Exception ex)
        {
            EngineStatus = "Falha ao iniciar o motor: " + ex.Message;
        }
        await trending;
    }
}
