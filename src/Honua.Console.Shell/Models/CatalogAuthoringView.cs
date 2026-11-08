namespace Honua.Console.Shell.Models;

public sealed record CatalogAdminRequest(string? CatalogId)
{
    public string? Section { get; init; }
    public string? TemplateId { get; init; }
    public string? ItemId { get; init; }
}

public sealed record CatalogTemplateList(string CatalogId, IReadOnlyList<CatalogTemplateRow> Templates);

public sealed record CatalogTemplateRow(string Id, string Title, string PageKind)
{
    public string? State { get; init; }
    public string? DraftVersion { get; init; }
    public string? ReleasedVersion { get; init; }
}

public sealed record CatalogTemplateDocument(string CatalogId, string TemplateId, string Title)
{
    public string? State { get; init; }
    public string? Version { get; init; }
    public string Source { get; init; } = string.Empty;
}

public sealed record CatalogListingDocument(string CatalogId, string ItemId, string Title)
{
    public string? Kind { get; init; }
    public bool? CanAppear { get; init; }
    public string? Summary { get; init; }
    public string? Description { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = [];
    public string? Licence { get; init; }
    public string? LicenceSource { get; init; }
    public string? UpdateFrequency { get; init; }
    public string? ThumbnailSource { get; init; }
    public string? ThumbnailAlt { get; init; }
    public string? Owner { get; init; }
    public string? Contact { get; init; }
    public string? ReviewState { get; init; }
    public string? DeprecationDate { get; init; }
    public string? Replacement { get; init; }
    public IReadOnlyList<CatalogListingMembership> Memberships { get; init; } = [];
    public IReadOnlyList<string> RelatedTitles { get; init; } = [];
    public IReadOnlyList<CatalogListingCheck> Checks { get; init; } = [];
}

public sealed record CatalogListingMembership(string CatalogId, string Title, string Reason)
{
    public string? Audience { get; init; }
    public string? Visibility { get; init; }
    public string? Category { get; init; }
    public bool? Featured { get; init; }
    public bool? HiddenHere { get; init; }
}

public sealed record CatalogListingCheck(string Label, string State);
