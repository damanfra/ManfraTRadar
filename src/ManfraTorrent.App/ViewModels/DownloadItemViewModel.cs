using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ManfraTorrent.App.Services;
using ManfraTorrent.Core.Services;
using MonoTorrent;
using MonoTorrent.Client;

namespace ManfraTorrent.App.ViewModels;

public partial class DownloadItemViewModel : ViewModelBase
{
    readonly TorrentEngineService engine;
    readonly Action onChanged;

    public DownloadItemViewModel(TorrentManager manager, TorrentEngineService engine, Action onChanged)
    {
        Manager = manager;
        this.engine = engine;
        this.onChanged = onChanged;
        Update();
    }

    public TorrentManager Manager { get; }

    [ObservableProperty] private string name = "";
    [ObservableProperty] private string statusText = "";
    [ObservableProperty] private double progressPercent;
    [ObservableProperty] private string leftText = "";
    [ObservableProperty] private string rightText = "";
    [ObservableProperty] private bool isComplete;
    [ObservableProperty] private bool isPaused;
    [ObservableProperty] private bool isRunning;
    [ObservableProperty] private bool isError;
    [ObservableProperty] private bool isBusy;

    public bool IsActive => IsRunning && !IsComplete;

    // ---- Estado em destaque (etiqueta colorida + ordem na aba Todos) ----
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(KindDownloading), nameof(KindMetadata), nameof(KindChecking),
        nameof(KindPaused), nameof(KindSeeding), nameof(KindDone), nameof(KindError))]
    private string stateKind = "";

    [ObservableProperty] private string stateLabel = "";

    /// <summary>Concluídos parados e pausados ficam mais apagados na lista.</summary>
    [ObservableProperty] private bool isDimmed;

    public bool KindDownloading => StateKind == "downloading";
    public bool KindMetadata => StateKind == "metadata";
    public bool KindChecking => StateKind == "checking";
    public bool KindPaused => StateKind == "paused";
    public bool KindSeeding => StateKind == "seeding";
    public bool KindDone => StateKind == "done";
    public bool KindError => StateKind == "error";

    /// <summary>Ordem na aba Todos: erro, baixando, metadados, pausado, semeando, concluído.</summary>
    public int SortGroup { get; private set; }
    public long DownloadRate { get; private set; }
    public long UploadRate { get; private set; }

    public void Update()
    {
        var m = Manager;
        var state = m.State;

        Name = engine.DisplayNameOf(m);

        var hasMetadata = m.HasMetadata;
        var total = m.Torrent?.Size ?? m.MagnetLink.Size ?? 0;
        var percent = hasMetadata && !double.IsNaN(m.Progress) ? m.Progress : 0;
        var done = (long) (total * percent / 100.0);

        DownloadRate = m.Monitor.DownloadRate;
        UploadRate = m.Monitor.UploadRate;

        ProgressPercent = percent;
        IsComplete = hasMetadata && m.Complete;
        IsError = state == TorrentState.Error;
        IsPaused = state is TorrentState.Stopped or TorrentState.Paused or TorrentState.HashingPaused;
        IsRunning = !IsPaused && !IsError;
        OnPropertyChanged(nameof(IsActive));

        (StateKind, StateLabel, SortGroup) = state switch
        {
            TorrentState.Error => ("error", "Erro", 0),
            TorrentState.Downloading => ("downloading", "Baixando", 1),
            TorrentState.Hashing or TorrentState.Starting or TorrentState.FetchingHashes
                or TorrentState.Stopping => ("checking", "Verificando", 1),
            TorrentState.Metadata => ("metadata", "Metadados", 2),
            TorrentState.Seeding => ("seeding", "Semeando", 4),
            _ when IsComplete => ("done", "Concluído", 5),
            _ => ("paused", "Pausado", 3),
        };
        IsDimmed = StateKind is "done" or "paused";

        var waiting = DateTime.Now - m.StartTime;
        StatusText = state switch
        {
            TorrentState.Metadata when m.OpenConnections == 0 && waiting > TimeSpan.FromMinutes(10)
                => $"Ninguém compartilhando ainda · procurando há {Format.Duration(waiting)}",
            TorrentState.Metadata => $"Buscando metadados há {Format.Duration(waiting)} · {m.OpenConnections} pares",
            TorrentState.Hashing => "Verificando arquivos…",
            TorrentState.Starting => "Iniciando…",
            TorrentState.Stopping => "Parando…",
            TorrentState.FetchingHashes => "Buscando hashes…",
            TorrentState.Downloading => $"{m.OpenConnections} pares conectados · {m.Peers.Seeds} seeds · {m.Peers.Leechs} leechers",
            TorrentState.Seeding => $"Compartilhando com {m.UploadingTo} par(es)",
            TorrentState.Error => m.Error?.Exception.Message ?? "Erro desconhecido",
            _ when IsComplete => "Parado · não está compartilhando",
            _ => "Pausado por você",
        };

        LeftText = hasMetadata
            ? IsComplete
                ? $"{Format.Bytes(total)} · enviado {Format.Bytes(m.Monitor.DataBytesSent)}"
                : $"{Format.Bytes(done)} de {Format.Bytes(total)} · {percent:0.0}%"
            : "aguardando metadados";

        RightText = state switch
        {
            TorrentState.Downloading => $"↓ {Format.Speed(DownloadRate)} · ↑ {Format.Speed(UploadRate)} · {Format.Eta(total - done, DownloadRate)}",
            TorrentState.Seeding => $"↑ {Format.Speed(UploadRate)}",
            TorrentState.Metadata => $"↓ {Format.Speed(DownloadRate)}",
            _ => "",
        };
    }

    [RelayCommand]
    private async Task TogglePause()
    {
        IsBusy = true;
        try
        {
            if (IsPaused || IsError) await engine.ResumeAsync(Manager);
            else await engine.PauseAsync(Manager);
        }
        finally
        {
            IsBusy = false;
            Update();
            onChanged();
        }
    }

    [RelayCommand]
    private void OpenFolder()
    {
        var path = Manager.ContainingDirectory;
        if (!Directory.Exists(path)) path = Manager.SavePath;
        if (Directory.Exists(path)) UiServices.Open(path);
    }

    [RelayCommand]
    private Task CopyMagnet() => UiServices.CopyAsync(Manager.MagnetLink.ToV1String());

    [RelayCommand]
    private Task Remove() => RemoveCore(deleteFiles: false);

    [RelayCommand]
    private Task RemoveWithFiles() => RemoveCore(deleteFiles: true);

    async Task RemoveCore(bool deleteFiles)
    {
        IsBusy = true;
        try
        {
            await engine.RemoveAsync(Manager, deleteFiles);
        }
        finally
        {
            IsBusy = false;
            onChanged();
        }
    }
}
