namespace ManfraTorrent.Core.Models;

public enum Category
{
    All,
    Movies,
    Series,
    Software,
    Games,
    Books,
    Music,
}

public sealed record CategoryOption(Category Value, string Label)
{
    public static IReadOnlyList<CategoryOption> AllOptions { get; } =
    [
        new(Category.All, "Tudo"),
        new(Category.Movies, "Filmes"),
        new(Category.Series, "Séries"),
        new(Category.Software, "Software"),
        new(Category.Games, "Jogos"),
        new(Category.Books, "Livros"),
        new(Category.Music, "Música"),
    ];

    public static string LabelOf(Category category) =>
        AllOptions.FirstOrDefault(o => o.Value == category)?.Label ?? "Outros";

    public override string ToString() => Label;
}
