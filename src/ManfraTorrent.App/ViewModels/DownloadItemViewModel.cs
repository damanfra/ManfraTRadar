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
    public long DownloadRate { get; private set; }
    public long UploadRate { get; private set; }

    public void Update()
    {
        var m = Manager;
        var state = m.State;

        Name = string.IsNullOrEmpty(m.Name) ? TorrentEngineService.KeyOf(m) : m.Name;

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

        StatusText = state switch
        {
            TorrentState.Metadata => $"Buscando metadados… · {m.OpenConnections} pares",
            TorrentState.Hashing => "Verificando arquivos…",
            TorrentState.Starting => "Iniciando…",
            TorrentState.Stopping => "Parando…",
            TorrentState.FetchingHashes => "Buscando hashes…",
            TorrentState.Downloading => $"{m.OpenConnections} pares conectados · {m.Peers.Seeds} seeds · {m.Peers.Leechs} leechers",
            TorrentState.Seeding => $"Concluído · semeando para {m.UploadingTo} par(es)",
            TorrentState.Error => "Erro: " + (m.Error?.Exception.Message ?? "desconhecido"),
            _ when IsComplete => "Concluído · parado",
            _ => "Pausado",
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
