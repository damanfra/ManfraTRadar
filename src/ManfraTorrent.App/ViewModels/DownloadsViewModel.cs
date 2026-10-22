using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ManfraTorrent.App.Services;
using ManfraTorrent.Core.Services;
using ManfraTorrent.Core.Subtitles;
using MonoTorrent.Client;

namespace ManfraTorrent.App.ViewModels;

public partial class DownloadsViewModel : ViewModelBase
{
    readonly SettingsStore store;
    readonly TorrentEngineService engine;
    readonly List<DownloadItemViewModel> items = [];
    DispatcherTimer? timer;
    int ticks;

    readonly SubtitleService subtitles;

    public DownloadsViewModel(SettingsStore store, TorrentEngineService engine, SubtitleService subtitles)
    {
        this.store = store;
        this.engine = engine;
        this.subtitles = subtitles;
    }

    async Task OpenSubtitlesAsync(DownloadItemViewModel item)
    {
        var files = item.Manager.Files.Select(f => f.FullPath).ToList();
        await UiServices.ShowSubtitlesAsync(new SubtitleViewModel(subtitles, item.Name, files));
    }

    public ObservableCollection<DownloadItemViewModel> Visible { get; } = [];
    public IReadOnlyList<string> Filters { get; } = ["Todos", "Ativos", "Concluídos"];

    [ObservableProperty] private string filter = "Todos";
    [ObservableProperty] private string addInput = "";
    [ObservableProperty] private bool addSequential;
    [ObservableProperty] private string statusText = "";
    [ObservableProperty] private string totalDownText = "0 B/s";
    [ObservableProperty] private string totalUpText = "0 B/s";
    [ObservableProperty] private int activeCount;
    [ObservableProperty] private bool isAdding;

    public bool HasActive => ActiveCount > 0;
    public bool IsEmpty => Visible.Count == 0;
    public string FolderText => "Pasta: " + store.Settings.DownloadFolder;

    partial void OnActiveCountChanged(int value) => OnPropertyChanged(nameof(HasActive));

    partial void OnFilterChanged(string value)
    {
        if (value is null) { Filter = "Todos"; return; }
        Refresh();
    }

    public void Start()
    {
        timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) => Tick();
        timer.Start();
        Refresh();
    }

    void Tick()
    {
        Refresh();

        // Salva o estado do motor a cada minuto, para não perder progresso se o app cair.
        if (++ticks % 60 == 0)
            _ = SafeSaveAsync();
    }

    async Task SafeSaveAsync()
    {
        try { await engine.SaveStateAsync(); } catch { /* tenta de novo no próximo minuto */ }
    }

    /// <summary>Sincroniza a lista com o motor e atualiza números. Chamado pelo timer e após ações.</summary>
    public void Refresh()
    {
        var managers = engine.Managers;

        items.RemoveAll(i => !managers.Contains(i.Manager));
        foreach (var m in managers)
        {
            if (items.All(i => i.Manager != m))
                items.Add(new DownloadItemViewModel(m, engine, Refresh, OpenSubtitlesAsync));
        }

        long down = 0, up = 0;
        foreach (var item in items)
        {
            item.Update();
            down += item.DownloadRate;
            up += item.UploadRate;
        }
        TotalDownText = Format.Speed(down);
        TotalUpText = Format.Speed(up);
        ActiveCount = items.Count(i => i.IsActive);
        OnPropertyChanged(nameof(FolderText));

        var wanted = Filter switch
        {
            "Ativos" => items.Where(i => !i.IsComplete),
            "Concluídos" => items.Where(i => i.IsComplete),
            _ => items,
        };
        // Agrupa por estado (OrderBy é estável: mantém a ordem de chegada dentro de cada grupo).
        var list = wanted.OrderBy(i => i.SortGroup).ToList();
        if (!list.SequenceEqual(Visible))
        {
            Visible.Clear();
            foreach (var i in list) Visible.Add(i);
            OnPropertyChanged(nameof(IsEmpty));
        }
    }

    [RelayCommand]
    private Task Add() => AddCore(AddInput);

    [RelayCommand]
    private async Task OpenTorrentFile()
    {
        var path = await UiServices.PickTorrentFileAsync();
        if (path is not null) await AddCore(path);
    }

    async Task AddCore(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return;
        IsAdding = true;
        StatusText = "Adicionando…";
        try
        {
            TorrentManager manager = await engine.AddAsync(input, AddSequential);
            StatusText = $"Adicionado: {(string.IsNullOrEmpty(manager.Name) ? "(buscando nome…)" : manager.Name)}";
            AddInput = "";
            Refresh();
        }
        catch (Exception ex)
        {
            StatusText = "Não foi possível adicionar: " + ex.Message;
        }
        finally
        {
            IsAdding = false;
        }
    }

    [RelayCommand]
    private void OpenDownloadFolder()
    {
        Directory.CreateDirectory(store.Settings.DownloadFolder);
        UiServices.Open(store.Settings.DownloadFolder);
    }
}
