using Honua.Console.Contracts;

namespace Honua.Console.Shell.Services;

public interface IConsoleCatalogClient
{
    Task<CatalogSearchResult> SearchAsync(CatalogListRequest request, CancellationToken cancellationToken = default);

    Task<CatalogItemReadResult> GetCatalogItemAsync(
        string idOrSlug,
        CatalogReadContext context,
        CancellationToken cancellationToken = default);

    Task<CatalogItemReadResult> GetOpenDataItemAsync(
        string idOrSlug,
        CancellationToken cancellationToken = default);

    Task<MapPackageReadResult> GetMapPackageAsync(
        string mapId,
        CatalogReadContext context,
        CancellationToken cancellationToken = default);

    Task<MapPackageReadResult> GetDraftMapAsync(
        string sourceItemId,
        CancellationToken cancellationToken = default);

    Task<MapPackageReadResult> AuthorizeEmbedAsync(
        string mapId,
        EmbedRouteOptions options,
        CancellationToken cancellationToken = default);
}

public sealed record CatalogSearchResult(
    IReadOnlyList<ConsoleContentSummary> Items,
    IReadOnlyDictionary<string, int> TypeCounts,
    CatalogListRequest Request);

public sealed record CatalogReadContext
{
    public bool Anonymous { get; init; }

    public string PublicLinkToken { get; init; } = string.Empty;

    public static CatalogReadContext Authenticated { get; } = new();

    public static CatalogReadContext AnonymousPublicLink(string? token) =>
        new()
        {
            Anonymous = true,
            PublicLinkToken = token?.Trim() ?? string.Empty
        };
}

public sealed record CatalogItemReadResult
{
    public CatalogReadStatus Status { get; init; }

    public ConsoleContentDetail? Item { get; init; }

    public string Message { get; init; } = string.Empty;

    public bool AnonymousRead { get; init; }

    public static CatalogItemReadResult Allowed(ConsoleContentDetail item, bool anonymousRead = false) =>
        new()
        {
            Status = CatalogReadStatus.Allowed,
            Item = item,
            AnonymousRead = anonymousRead
        };

    public static CatalogItemReadResult Denied(CatalogReadStatus status, string message) =>
        new()
        {
            Status = status,
            Message = message
        };
}

public sealed record MapPackageReadResult
{
    public CatalogReadStatus Status { get; init; }

    public ConsoleMapPackage? MapPackage { get; init; }

    public string Message { get; init; } = string.Empty;

    public bool AnonymousRead { get; init; }

    public static MapPackageReadResult Allowed(ConsoleMapPackage mapPackage, bool anonymousRead = false) =>
        new()
        {
            Status = CatalogReadStatus.Allowed,
            MapPackage = mapPackage,
            AnonymousRead = anonymousRead
        };

    public static MapPackageReadResult Denied(CatalogReadStatus status, string message) =>
        new()
        {
            Status = status,
            Message = message
        };
}

public enum CatalogReadStatus
{
    Allowed,
    Missing,
    Forbidden,
    Unavailable,
    UnsupportedServiceMetadata,
    UnsupportedPackageBinding
}
