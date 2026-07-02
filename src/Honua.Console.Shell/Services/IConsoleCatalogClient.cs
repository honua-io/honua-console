using Honua.Console.Contracts;

namespace Honua.Console.Shell.Services;

public interface IConsoleCatalogClient
{
    Task<CatalogSearchResult> SearchAsync(
        CatalogListRequest request,
        CatalogReadContext context,
        CancellationToken cancellationToken = default);

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
        CatalogReadContext context,
        CancellationToken cancellationToken = default);

    Task<MapPackageReadResult> AuthorizeEmbedAsync(
        string mapId,
        EmbedRouteOptions options,
        CancellationToken cancellationToken = default);
}

public sealed record CatalogSearchResult(
    IReadOnlyList<ConsoleContentSummary> Items,
    IReadOnlyDictionary<string, int> TypeCounts,
    CatalogListRequest Request)
{
    /// <summary>
    /// Read status for the search. <see cref="CatalogReadStatus.Allowed"/> means the search ran — even if it
    /// returned zero items, which is a genuine empty catalog. Any other value means the search FAILED against
    /// the server (outage/timeout/permission/contract error) and <see cref="Items"/> is empty because the read
    /// could not complete, not because the catalog is empty. The Catalog page renders a distinct error/retry
    /// state for the failed case so a live-server failure is never shown as a successful empty catalog
    /// (issue #272).
    /// </summary>
    public CatalogReadStatus Status { get; init; } = CatalogReadStatus.Allowed;

    /// <summary>Operator-facing failure detail when <see cref="Status"/> is not Allowed; empty on success.</summary>
    public string FailureMessage { get; init; } = string.Empty;

    /// <summary>True when the search ran successfully (including a genuine empty catalog with zero items).</summary>
    public bool Succeeded => Status == CatalogReadStatus.Allowed;

    /// <summary>
    /// A typed failure result: an empty item set carrying the server read status/detail so the page can tell a
    /// failed read apart from a genuine empty catalog. Mirrors <c>CatalogItemReadResult.Denied</c> for the
    /// item/map read paths. A non-failure <paramref name="status"/> is coerced to
    /// <see cref="CatalogReadStatus.Unavailable"/> so a failed result is never mistaken for success.
    /// </summary>
    public static CatalogSearchResult Failed(CatalogListRequest request, CatalogReadStatus status, string message) =>
        new([], new Dictionary<string, int>(StringComparer.Ordinal), request)
        {
            Status = status == CatalogReadStatus.Allowed ? CatalogReadStatus.Unavailable : status,
            FailureMessage = message
        };
}

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
