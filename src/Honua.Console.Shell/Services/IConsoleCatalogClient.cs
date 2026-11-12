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

    Task<MapReadResult> GetMapAsync(
        string mapId,
        CatalogReadContext context,
        CancellationToken cancellationToken = default);

    Task<MapReadResult> GetDraftMapAsync(
        string sourceItemId,
        CatalogReadContext context,
        CancellationToken cancellationToken = default);

    Task<MapReadResult> AuthorizeEmbedAsync(
        string mapId,
        EmbedRouteOptions options,
        CancellationToken cancellationToken = default);
}

public sealed record CatalogSearchResult(
    IReadOnlyList<ConsoleContentSummary> Items,
    IReadOnlyDictionary<string, int> TypeCounts,
    CatalogListRequest Request)
{
    // Allowed means the search ran, including zero items; any other status means the read failed and the empty item set is not an empty catalog.
    public CatalogReadStatus Status { get; init; } = CatalogReadStatus.Allowed;

    public string FailureMessage { get; init; } = string.Empty;

    public bool Succeeded => Status == CatalogReadStatus.Allowed;

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

public sealed record MapReadResult
{
    public CatalogReadStatus Status { get; init; }

    public ConsoleMap? Map { get; init; }

    public string Message { get; init; } = string.Empty;

    public bool AnonymousRead { get; init; }

    public static MapReadResult Allowed(ConsoleMap map, bool anonymousRead = false) =>
        new()
        {
            Status = CatalogReadStatus.Allowed,
            Map = map,
            AnonymousRead = anonymousRead
        };

    public static MapReadResult Denied(CatalogReadStatus status, string message) =>
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
    UnsupportedContentBinding
}
