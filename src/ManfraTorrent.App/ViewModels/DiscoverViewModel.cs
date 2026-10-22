using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ManfraTorrent.App.Services;
using ManfraTorrent.Core.Models;
using ManfraTorrent.Core.Providers;
using ManfraTorrent.Core.Services;

namespace ManfraTorrent.App.ViewModels;

public partial class DiscoverViewModel : ViewModelBase
{
    readonly SettingsStore store;
    readonly TorrentEngineService engine;
    readonly DownloadsViewModel downloads;
    List<SearchResult> raw = [];
    CancellationTokenSource? cts;

    public DiscoverViewModel(SettingsStore store, TorrentEngineService engine, DownloadsViewModel downloads)
    {
        this.store = store;
        this.engine = engine;
        this.downloads = downloads;
        selectedCategory = Categories[0];
    }

    public IReadOnlyList<CategoryOption> Categories => CategoryOption.AllOptions;
    public IReadOnlyList<string> SortModes { get; } = [Relevance, "Mais populares", "Mais novos", "Maiores", "A–Z"];

    const string Relevance = "Relevância";
    const string Popular = "Mais populares";
    public ObservableCollection<SearchResultViewModel> Results { get; } = [];

    [ObservableProperty] private CategoryOption selectedCategory;
    [ObservableProperty] private string sortMode = "Mais populares";
    [ObservableProperty] private string searchText = "";
    [ObservableProperty] private string headerText = "Em alta hoje";
    [ObservableProperty] private string statusText = "";
    [ObservableProperty] private bool isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    private SearchResultViewModel? selected;

    public bool HasSelection => Selected is not null;
    public bool IsEmpty => !IsBusy && Results.Count == 0;

    partial void OnSelectedCategoryChanged(CategoryOption value)
    {
        if (value is null)
        {
            SelectedCategory = Categories[0]; // chip desmarcado: volta para "Tudo"
            return;
        }
        _ = RefreshAsync();
    }
    partial void OnSortModeChanged(string value) => ApplySort();
    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(IsEmpty));

    [RelayCommand]
    private Task Search()
    {
        // Numa busca, a ordem que a fonte devolve (relevância) é a mais útil.
        if (SearchText.Trim().Length > 0) SortMode = Relevance;
        return RefreshAsync();
    }

    [RelayCommand]
    private Task ClearSearch()
    {
        SearchText = "";
        if (SortMode == Relevance) SortMode = Popular;
        return RefreshAsync();
    }

    public async Task RefreshAsync()
    {
        cts?.Cancel();
        var local = new CancellationTokenSource();
        cts = local;
        var ct = local.Token;

        var providers = ProviderFactory.Create(store.Settings.Sources);
        var query = SearchText.Trim();
        var category = SelectedCategory?.Value ?? Category.All;
        HeaderText = query.Length == 0 ? "Em alta hoje" : $"Resultados para “{query}”";

        if (providers.Count == 0)
        {
            raw = [];
            ApplySort();
            StatusText = "Nenhuma fonte ativa. Ative ou cadastre uma em Fontes.";
            return;
        }

        IsBusy = true;
        StatusText = $"Consultando {providers.Count} fonte(s)…";

        var tasks = providers.Select(p => QueryProviderAsync(p, query, category, ct)).ToList();
        var outcomes = await Task.WhenAll(tasks);
        if (ct.IsCancellationRequested) return;

        var filtered = ResultFilter.Apply(outcomes.SelectMany(o => o.Items), store.Settings);
        raw = filtered.Items;
        ApplySort();

        var errors = outcomes.Where(o => o.Error is not null).Select(o => $"{o.Name}: {o.Error}").ToList();
        StatusText = $"{raw.Count} resultado(s) de {providers.Count - errors.Count}/{providers.Count} fonte(s)"
                     + (filtered.Duplicates > 0 ? $" · {filtered.Duplicates} repetido(s) agrupado(s)" : "")
                     + (filtered.Hidden > 0 ? $" · {filtered.Hidden} oculto(s) pelos filtros" : "")
                     + (errors.Count > 0 ? " — falhou: " + string.Join(" | ", errors) : "");
        IsBusy = false;
    }

    sealed record Outcome(string Name, IReadOnlyList<SearchResult> Items, string? Error);

    static async Task<Outcome> QueryProviderAsync(ITorrentProvider p, string query, Category category, CancellationToken ct)
    {
        try
        {
            var items = query.Length == 0
                ? await p.GetTrendingAsync(category, ct)
                : await p.SearchAsync(query, category, ct);
            return new Outcome(p.Name, items, null);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return new Outcome(p.Name, [], null);
        }
        catch (Exception ex)
        {
            return new Outcome(p.Name, [], ex.Message);
        }
    }

    /// <summary>
    /// Mantém a ordem de relevância de cada fonte e intercala as fontes
    /// (1º de cada, depois 2º de cada...), para nenhuma dominar o topo.
    /// </summary>
    static IEnumerable<SearchResult> InterleaveBySource(List<SearchResult> items)
    {
        var groups = items.GroupBy(r => r.Source).Select(g => g.ToList()).ToList();
        var max = groups.Count == 0 ? 0 : groups.Max(g => g.Count);
        for (var i = 0; i < max; i++)
            foreach (var g in groups)
                if (i < g.Count)
                    yield return g[i];
    }

    void ApplySort()
    {
        IEnumerable<SearchResult> sorted = SortMode switch
        {
            Relevance => InterleaveBySource(raw),
            "Mais novos" => raw.OrderByDescending(r => r.Published ?? DateTimeOffset.MinValue),
            "Maiores" => raw.OrderByDescending(r => r.SizeBytes ?? 0),
            "A–Z" => raw.OrderBy(r => r.Title, StringComparer.CurrentCultureIgnoreCase),
            _ => raw.OrderByDescending(r => r.Score),
        };

        var previous = Selected?.Model;
        Results.Clear();
        var rank = 1;
        foreach (var r in sorted)
            Results.Add(new SearchResultViewModel(r, rank++));

        Selected = previous is null ? null : Results.FirstOrDefault(r => r.Model == previous);
        OnPropertyChanged(nameof(IsEmpty));
    }

    [RelayCommand]
    private Task Download(SearchResultViewModel? item) => AddAsync(item, sequential: false);

    [RelayCommand]
    private Task DownloadSequential(SearchResultViewModel? item) => AddAsync(item, sequential: true);

    async Task AddAsync(SearchResultViewModel? item, bool sequential)
    {
        if (item?.Model.BestLink is not { } link) return;
        item.IsAdding = true;
        try
        {
            var manager = await engine.AddAsync(link, sequential, displayName: item.Model.Title);
            item.Added = true;
            StatusText = $"Adicionado aos downloads: {item.Model.Title}";
            downloads.Refresh();
        }
        catch (Exception ex)
        {
            StatusText = "Falha ao adicionar: " + ex.Message;
        }
        finally
        {
            item.IsAdding = false;
        }
    }

    [RelayCommand]
    private async Task CopyLink(SearchResultViewModel? item)
    {
        if (item?.Model.BestLink is not { } link) return;
        await UiServices.CopyAsync(link);
        StatusText = "Link copiado.";
    }

    [RelayCommand]
    private void OpenPage(SearchResultViewModel? item)
    {
        if (item?.Model.DetailsUrl is { } url) UiServices.Open(url);
    }

    [RelayCommand]
    private void CloseDetails() => Selected = null;
}
