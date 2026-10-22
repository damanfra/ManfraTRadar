using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ManfraTorrent.App.Services;
using ManfraTorrent.Core.Services;
using ManfraTorrent.Core.Subtitles;

namespace ManfraTorrent.App.ViewModels;

/// <summary>Um vídeo do download, com o que a verificação local encontrou.</summary>
public sealed class VideoEntryViewModel(VideoInfo info)
{
    public VideoInfo Info { get; } = info;
    public string Name => Info.FileName;
    public string SizeText => Format.Bytes(Info.Size);
    public bool HasPortuguese => Info.HasPortugueseLocally;

    public string LocalStatus => Info.LocalFindings.Count > 0
        ? string.Join("\n", Info.LocalFindings)
        : "Nenhuma legenda encontrada no disco.";

    public string Label => HasPortuguese ? $"{Name}  ·  já tem legenda PT" : Name;
    public override string ToString() => Label;
}

/// <summary>Uma legenda encontrada, pronta para a lista.</summary>
public sealed class CandidateViewModel(SubtitleCandidate model, string quotaNote)
{
    public SubtitleCandidate Model { get; } = model;
    public string Release => string.IsNullOrWhiteSpace(Model.Release) ? "(sem nome de release)" : Model.Release;
    public string Provider => Model.Provider;
    public string QuotaNote { get; } = quotaNote;
    public bool HashMatch => Model.HashMatch;
    public bool SameGroup => Model.SameGroup && !Model.HashMatch;

    public string Details
    {
        get
        {
            var parts = new List<string> { Model.Provider };
            if (!string.IsNullOrWhiteSpace(Model.Language)) parts.Add(Model.Language);
            if (Model.Downloads > 0) parts.Add($"{Format.Number(Model.Downloads)} downloads");
            if (!string.IsNullOrWhiteSpace(Model.Uploader)) parts.Add($"por {Model.Uploader}");
            if (Model.FullSeason) parts.Add("temporada completa");
            if (Model.HearingImpaired) parts.Add("para surdos (SDH)");
            return string.Join(" · ", parts);
        }
    }
}

/// <summary>
/// Janela de legendas, em etapas: (1) verificação local sem internet, (2) confirmação com o que será
/// consultado, (3) resultados, (4) baixar só a escolhida. Nada acontece sem um clique do usuário.
/// </summary>
public partial class SubtitleViewModel : ViewModelBase
{
    readonly SubtitleService service;
    readonly IReadOnlyList<string> torrentFiles;
    CancellationTokenSource? cts;

    public SubtitleViewModel(SubtitleService service, string downloadName, IReadOnlyList<string> torrentFiles)
    {
        this.service = service;
        this.torrentFiles = torrentFiles;
        DownloadName = downloadName;
    }

    public event Action? CloseRequested;

    public string DownloadName { get; }
    public ObservableCollection<VideoEntryViewModel> Videos { get; } = [];
    public ObservableCollection<CandidateViewModel> Candidates { get; } = [];

    [ObservableProperty] private bool isInspecting = true;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string summaryText = "Verificando os arquivos no disco…";
    [ObservableProperty] private string quotaText = "";
    [ObservableProperty] private string statusText = "";
    [ObservableProperty] private string resultsHeader = "";
    [ObservableProperty] private bool hasResults;
    [ObservableProperty] private bool canSearch;
    [ObservableProperty] private bool canSearchAgain;
    [ObservableProperty] private string searchButtonText = "Buscar";
    [ObservableProperty] private string? savedPath;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMultipleVideos))]
    private int videoCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LocalStatus))]
    private VideoEntryViewModel? selectedVideo;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanDownload), nameof(DownloadButtonText))]
    private CandidateViewModel? selectedCandidate;

    public bool HasMultipleVideos => VideoCount > 1;
    public string LocalStatus => SelectedVideo?.LocalStatus ?? "";
    public bool HasSaved => SavedPath is not null;
    public bool CanDownload => SelectedCandidate is not null && !IsBusy;

    public string DownloadButtonText => SelectedCandidate is null
        ? "Escolha uma legenda na lista"
        : $"Baixar esta legenda — {SelectedCandidate.QuotaNote}";

    partial void OnSavedPathChanged(string? value) => OnPropertyChanged(nameof(HasSaved));
    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(CanDownload));
    partial void OnSelectedVideoChanged(VideoEntryViewModel? value) => PrepareForVideo();

    /// <summary>Etapa 1: só disco local (lista vídeos, legendas existentes, faixas do MKV, hash).</summary>
    public async Task InitializeAsync()
    {
        try
        {
            var infos = await Task.Run(() =>
                VideoInspector.FindVideos(torrentFiles)
                    .Select(v => VideoInspector.Inspect(v, torrentFiles))
                    .ToList());

            foreach (var info in infos) Videos.Add(new VideoEntryViewModel(info));
            VideoCount = Videos.Count;

            if (Videos.Count == 0)
            {
                SummaryText = "Este download não tem arquivo de vídeo (ou os arquivos não estão mais na pasta).";
                return;
            }

            // Começa pelo primeiro vídeo que ainda não tem legenda em português.
            SelectedVideo = Videos.FirstOrDefault(v => !v.HasPortuguese) ?? Videos[0];
        }
        catch (Exception ex)
        {
            SummaryText = "Não consegui ler os arquivos: " + ex.Message;
        }
        finally
        {
            IsInspecting = false;
        }
    }

    /// <summary>Etapa 2: mostra o que será consultado; se houver cache, mostra os resultados guardados.</summary>
    void PrepareForVideo()
    {
        Candidates.Clear();
        SelectedCandidate = null;
        HasResults = false;
        SavedPath = null;
        StatusText = "";
        QuotaText = service.OpenSubtitlesQuotaText();

        var video = SelectedVideo?.Info;
        if (video is null)
        {
            CanSearch = false;
            return;
        }

        var providers = service.Providers();
        if (providers.Count == 0)
        {
            SummaryText = "Nenhum serviço de legendas configurado. Preencha a chave do OpenSubtitles e/ou do SubDL " +
                          "em Fontes e ajustes → Legendas.";
            CanSearch = false;
            CanSearchAgain = false;
            return;
        }

        var p = video.Parsed;
        var what = p.IsEpisode
            ? $"“{p.Title}” S{p.Season:00}E{p.Episode:00}"
            : p.Year is { } y ? $"“{p.Title}” ({y})" : $"“{p.Title}”";

        var cached = service.GetCached(video);
        if (cached is not null)
        {
            SummaryText = $"Busca já feita em {cached.SearchedAt.ToLocalTime():dd/MM HH:mm} para {what}. " +
                          "Mostrando os resultados guardados, sem consultar a internet.";
            ShowResults(cached);
            CanSearch = false;
            CanSearchAgain = true;
            return;
        }

        var services = string.Join(" e ", providers.Select(x => $"{x.Name} (1 busca)"));
        var hashNote = video.OsHash is null ? "" : " usando a impressão digital do arquivo, para achar legendas sincronizadas";
        SummaryText = $"Vai consultar {services} por {what}{hashNote}. " +
                      "Buscar não gasta cota; nenhuma legenda é baixada até você escolher.";
        SearchButtonText = video.HasPortugueseLocally ? "Buscar mesmo assim" : "Buscar";
        CanSearch = true;
        CanSearchAgain = false;
    }

    [RelayCommand]
    private Task Search() => RunSearchAsync();

    [RelayCommand]
    private Task SearchAgain() => RunSearchAsync();

    async Task RunSearchAsync()
    {
        var video = SelectedVideo?.Info;
        if (video is null || IsBusy) return;

        IsBusy = true;
        CanSearch = false;
        StatusText = "Buscando…";
        cts = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        try
        {
            var result = await service.SearchAsync(video, cts.Token);
            ShowResults(result);
            CanSearchAgain = true;
        }
        catch (Exception ex)
        {
            StatusText = "Falha na busca: " + ex.Message;
            CanSearch = true;
        }
        finally
        {
            IsBusy = false;
        }
    }

    void ShowResults(CachedSearch result)
    {
        Candidates.Clear();
        foreach (var c in result.Candidates)
            Candidates.Add(new CandidateViewModel(c, service.ProviderFor(c)?.QuotaNote ?? c.Provider));

        HasResults = Candidates.Count > 0;
        var synced = Candidates.Count(c => c.HashMatch);
        ResultsHeader = Candidates.Count == 0
            ? "Nenhuma legenda PT-BR encontrada."
            : $"{Candidates.Count} legenda(s)" + (synced > 0 ? $" · {synced} sincronizada(s) com o seu arquivo" : "");

        StatusText = result.Errors.Count > 0 ? "Alguns serviços falharam: " + string.Join(" | ", result.Errors) : "";
        SelectedCandidate = Candidates.FirstOrDefault();
    }

    /// <summary>Etapa 4: baixa só a legenda escolhida (1 da cota, se for do OpenSubtitles).</summary>
    [RelayCommand]
    private async Task Download()
    {
        var video = SelectedVideo?.Info;
        var candidate = SelectedCandidate?.Model;
        if (video is null || candidate is null || IsBusy) return;

        IsBusy = true;
        StatusText = "Baixando a legenda…";
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            SavedPath = await service.DownloadAndSaveAsync(video, candidate, timeout.Token);
            StatusText = "Legenda salva.";
        }
        catch (SubtitleQuotaException ex)
        {
            StatusText = ex.Message;
        }
        catch (Exception ex)
        {
            StatusText = "Falha ao baixar: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
            QuotaText = service.OpenSubtitlesQuotaText();
        }
    }

    [RelayCommand]
    private void OpenFolder()
    {
        var dir = Path.GetDirectoryName(SavedPath ?? SelectedVideo?.Info.Path ?? "");
        if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) UiServices.Open(dir);
    }

    [RelayCommand]
    private void OpenPage(CandidateViewModel? c)
    {
        if (c?.Model.PageUrl is { } url) UiServices.Open(url);
    }

    [RelayCommand]
    private void Close()
    {
        cts?.Cancel();
        CloseRequested?.Invoke();
    }
}
