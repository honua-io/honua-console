namespace Honua.Console.Shell.Models;

public sealed record CatalogArrangementDocument(
    string CatalogId,
    IReadOnlyList<CatalogCategoryRow> Categories,
    IReadOnlyList<CatalogFeaturedRow> Featured);

public sealed record CatalogCategoryRow(string Id, string Title, string Rule)
{
    public int? ItemCount { get; init; }
    public bool? ShowOnHome { get; init; }
}

public sealed record CatalogFeaturedRow(string Id, string Title)
{
    public string? Kind { get; init; }
    public string? Health { get; init; }
    public string? FeaturedUntil { get; init; }
    public string? AddedBy { get; init; }
}

public sealed record CatalogOrderMove(int From, int To);
