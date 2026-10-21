using CommunityToolkit.Mvvm.ComponentModel;
using ManfraTorrent.Core.Models;
using ManfraTorrent.Core.Services;

namespace ManfraTorrent.App.ViewModels;

public partial class SearchResultViewModel : ViewModelBase
{
    public SearchResultViewModel(SearchResult model, int rank)
    {
        Model = model;
        Rank = rank;
    }

    public SearchResult Model { get; }
    public int Rank { get; }

    public string RankText => Rank.ToString();
    public string Title => Model.Title;
    public string CategoryLabel => CategoryOption.LabelOf(Model.Category);
    public string SizeText => Format.Bytes(Model.SizeBytes);
    public string Meta => $"{CategoryLabel} · {SizeText} · {Model.Source}";
    public string AgeText => Format.Age(Model.Published);
    public string SourceLine => string.IsNullOrEmpty(AgeText) ? Model.Source : $"{Model.Source} · publicado {AgeText}";

    public bool HasSwarm => Model.Seeders is not null;
    public string SeedsText => "▲ " + Format.Number(Model.Seeders ?? 0);
    public string LeechText => "▼ " + Format.Number(Model.Leechers ?? 0);
    public string PopularityText => Model.Popularity is { } p ? $"{Format.Number(p)} downloads/sem." : "";

    public string StatALabel => HasSwarm ? "Seeders" : "Downloads/sem.";
    public string StatAValue => HasSwarm ? Format.Number(Model.Seeders ?? 0) : Format.Number(Model.Popularity ?? 0);
    public string StatBLabel => HasSwarm ? "Leechers" : "Fonte";
    public string StatBValue => HasSwarm ? Format.Number(Model.Leechers ?? 0) : "HTTP + P2P";

    /// <summary>0–100: proporção seeders/(seeders+leechers), suavizada pelo volume.</summary>
    public double HealthPercent
    {
        get
        {
            if (!HasSwarm) return 100;
            var s = Model.Seeders ?? 0;
            var l = Model.Leechers ?? 0;
            if (s == 0) return 2;
            var ratio = s / (double) (s + l);
            var volume = Math.Min(1.0, Math.Log10(s + 1) / 2.5);
            return Math.Round(100 * (0.6 * ratio + 0.4 * volume));
        }
    }

    public string HealthText
    {
        get
        {
            if (!HasSwarm) return "Internet Archive sempre semeia pelo próprio servidor (web seed).";
            var s = Model.Seeders ?? 0;
            var l = Math.Max(1, Model.Leechers ?? 0);
            var label = HealthPercent switch
            {
                >= 80 => "excelente",
                >= 55 => "boa",
                >= 30 => "regular",
                _ => "fraca",
            };
            return $"Saúde {label} · {s / (double) l:0.0} seeders por leecher";
        }
    }

    public string? InfoHash => Model.InfoHash;
    public bool HasInfoHash => !string.IsNullOrEmpty(Model.InfoHash);
    public bool HasDetailsUrl => !string.IsNullOrEmpty(Model.DetailsUrl);
    public bool CanDownload => Model.BestLink is not null;

    [ObservableProperty] private bool isAdding;
    [ObservableProperty] private bool added;
}
