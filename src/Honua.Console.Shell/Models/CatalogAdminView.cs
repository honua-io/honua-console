namespace Honua.Console.Shell.Models;

public sealed record CatalogAdminRow(string Id, string Title, string Audience, string ScopeLabel, string? ProposalHref);

public sealed record CatalogAdminVisibility(int Listed, int AudienceRestricted, int HiddenHere, int OptedOut, int Matched);

public sealed record CatalogAdminAttention(string Title, string Reason);

public sealed record CatalogAdminOverview(
    string CatalogId,
    string Title,
    CatalogAdminVisibility Visibility,
    IReadOnlyList<CatalogAdminAttention> Attention);

public sealed record CatalogAdminRead(
    IReadOnlyList<CatalogAdminRow> Catalogs,
    CatalogAdminOverview? Overview,
    string State,
    string? Message);

public sealed record CatalogAdminOptions
{
    public bool Enabled { get; init; }
}
