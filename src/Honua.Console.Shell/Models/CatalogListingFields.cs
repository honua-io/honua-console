namespace Honua.Console.Shell.Models;

public sealed record CatalogListingFields
{
    public string Title { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Tags { get; set; } = string.Empty;
    public string UpdateFrequency { get; set; } = string.Empty;
    public string ThumbnailAlt { get; set; } = string.Empty;
    public string Owner { get; set; } = string.Empty;
    public string Contact { get; set; } = string.Empty;
    public string ReviewState { get; set; } = string.Empty;
    public string DeprecationDate { get; set; } = string.Empty;
    public string Replacement { get; set; } = string.Empty;
    public bool? CanAppear { get; set; }

    public static CatalogListingFields FromDocument(CatalogListingDocument listing) => new()
    {
        Title = listing.Title,
        Summary = listing.Summary ?? string.Empty,
        Description = listing.Description ?? string.Empty,
        Tags = string.Join(", ", listing.Tags),
        UpdateFrequency = listing.UpdateFrequency ?? string.Empty,
        ThumbnailAlt = listing.ThumbnailAlt ?? string.Empty,
        Owner = listing.Owner ?? string.Empty,
        Contact = listing.Contact ?? string.Empty,
        ReviewState = listing.ReviewState ?? string.Empty,
        DeprecationDate = listing.DeprecationDate ?? string.Empty,
        Replacement = listing.Replacement ?? string.Empty,
        CanAppear = listing.CanAppear
    };
}
