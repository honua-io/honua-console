namespace Honua.Console.Contracts;

// SHIM(honua-sdk-dotnet#166): Catalog, share-access, map-package, and embed
// route contracts are not yet projected to the .NET SDK. These records mirror
// the route-map contract used by honua-console#34 and are consumed through the
// single Console shim boundary until honua-console#7 swaps them for SDK types.
public static class CatalogQueryKeys
{
    public const string Query = "q";
    public const string Type = "type";
    public const string Tag = "tag";
    public const string Owner = "owner";
    public const string Visibility = "visibility";
    public const string Sort = "sort";
    public const string Cursor = "cursor";

    public static IReadOnlySet<string> PortalAllowed { get; } = new HashSet<string>(
        [Query, Type, Tag, Owner, Visibility, Sort, Cursor],
        StringComparer.Ordinal);
}

public static class CatalogSharingTiers
{
    public const string Private = "private";
    public const string Organization = "org";
    public const string Group = "group";
    public const string PublicLink = "public-link";
    public const string Public = "public";

    public static IReadOnlySet<string> All { get; } = new HashSet<string>(
        [Private, Organization, Group, PublicLink, Public],
        StringComparer.Ordinal);
}

public static class CatalogSortOptions
{
    public const string Relevance = "relevance";
    public const string ModifiedDesc = "modified-desc";
    public const string ModifiedAsc = "modified-asc";
    public const string TitleAsc = "title-asc";
    public const string TitleDesc = "title-desc";

    public static IReadOnlySet<string> All { get; } = new HashSet<string>(
        [Relevance, ModifiedDesc, ModifiedAsc, TitleAsc, TitleDesc],
        StringComparer.Ordinal);
}

public static class CatalogContentTypes
{
    public static IReadOnlyList<CatalogContentType> All { get; } =
    [
        new("dataset", "Datasets", "Tables, files, and reusable data products."),
        new("service", "Services", "Published service endpoints and APIs."),
        new("layer", "Layers", "Renderable feature, raster, and vector layers."),
        new("document", "Documents", "Public metadata documents and downloadable resources."),
        new("map", "Maps", "Saved maps and web map packages."),
        new("dashboard", "Dashboards", "Operational and analytical dashboards."),
        new("report", "Reports", "Static or scheduled analytical reports."),
        new("form", "Forms", "Field collection forms and surveys."),
        new("app", "Apps", "Generated or packaged applications."),
        new("workflow", "Workflows", "Automations, approvals, and jobs."),
        new("analysis", "Analyses", "Spatial analyses and derived outputs."),
        new("gp-service", "GP Services", "Geoprocessing services and tools."),
        new("etl-pipeline", "ETL Pipelines", "Extract, transform, and load pipelines."),
        new("connector", "Connectors", "External system connectors."),
        new("template", "Templates", "Reusable Studio and publishing templates.")
    ];

    public static bool Contains(string? key) =>
        !string.IsNullOrWhiteSpace(key)
        && All.Any(type => string.Equals(type.Key, key, StringComparison.Ordinal));

    public static string LabelFor(string? key) =>
        All.FirstOrDefault(type => string.Equals(type.Key, key, StringComparison.Ordinal))?.Label
        ?? "Content";
}

public sealed record CatalogContentType(string Key, string Label, string Description);

public sealed record CatalogSearchState
{
    public string Query { get; init; } = string.Empty;

    public string Type { get; init; } = string.Empty;

    public string Tag { get; init; } = string.Empty;

    public string Owner { get; init; } = string.Empty;

    public string Visibility { get; init; } = string.Empty;

    public string Sort { get; init; } = CatalogSortOptions.Relevance;

    public string Cursor { get; init; } = string.Empty;

    public IReadOnlyList<string> IgnoredQueryKeys { get; init; } = [];

    public static CatalogSearchState FromUri(string uri) => FromUri(new Uri(uri, UriKind.Absolute));

    public static CatalogSearchState FromUri(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);

        var query = ConsoleUrlQuery.Parse(uri.Query);
        var ignoredKeys = query.Keys
            .Where(key => !CatalogQueryKeys.PortalAllowed.Contains(key))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToArray();

        return new CatalogSearchState
        {
            Query = NormalizeLooseText(query.GetValueOrDefault(CatalogQueryKeys.Query)),
            Type = NormalizeType(query.GetValueOrDefault(CatalogQueryKeys.Type)),
            Tag = NormalizeLooseText(query.GetValueOrDefault(CatalogQueryKeys.Tag)),
            Owner = NormalizeLooseText(query.GetValueOrDefault(CatalogQueryKeys.Owner)),
            Visibility = NormalizeSharing(query.GetValueOrDefault(CatalogQueryKeys.Visibility)),
            Sort = NormalizeSort(query.GetValueOrDefault(CatalogQueryKeys.Sort)),
            Cursor = NormalizeLooseText(query.GetValueOrDefault(CatalogQueryKeys.Cursor)),
            IgnoredQueryKeys = ignoredKeys
        };
    }

    public CatalogListRequest ToListRequest() =>
        new()
        {
            Query = Query,
            Type = Type,
            Tag = Tag,
            Owner = Owner,
            Sharing = Visibility,
            Sort = Sort,
            Cursor = Cursor
        };

    public IReadOnlyDictionary<string, string> ToPortalQueryParameters()
    {
        var parameters = new Dictionary<string, string>(StringComparer.Ordinal);
        AddIfPresent(parameters, CatalogQueryKeys.Query, Query);
        AddIfPresent(parameters, CatalogQueryKeys.Type, Type);
        AddIfPresent(parameters, CatalogQueryKeys.Tag, Tag);
        AddIfPresent(parameters, CatalogQueryKeys.Owner, Owner);
        AddIfPresent(parameters, CatalogQueryKeys.Visibility, Visibility);
        if (!string.Equals(Sort, CatalogSortOptions.Relevance, StringComparison.Ordinal))
        {
            AddIfPresent(parameters, CatalogQueryKeys.Sort, Sort);
        }

        AddIfPresent(parameters, CatalogQueryKeys.Cursor, Cursor);
        return parameters;
    }

    public string ToPortalQueryString() => ConsoleUrlQuery.ToQueryString(ToPortalQueryParameters());

    private static void AddIfPresent(IDictionary<string, string> parameters, string key, string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            parameters[key] = value;
        }
    }

    private static string NormalizeLooseText(string? value) => value?.Trim() ?? string.Empty;

    private static string NormalizeType(string? value)
    {
        var trimmed = NormalizeLooseText(value);
        return CatalogContentTypes.Contains(trimmed) ? trimmed : string.Empty;
    }

    private static string NormalizeSharing(string? value)
    {
        var trimmed = NormalizeLooseText(value);
        return CatalogSharingTiers.All.Contains(trimmed) ? trimmed : string.Empty;
    }

    private static string NormalizeSort(string? value)
    {
        var trimmed = NormalizeLooseText(value);
        return CatalogSortOptions.All.Contains(trimmed) ? trimmed : CatalogSortOptions.Relevance;
    }
}

public sealed record CatalogListRequest
{
    public string Query { get; init; } = string.Empty;

    public string Type { get; init; } = string.Empty;

    public string Tag { get; init; } = string.Empty;

    public string Owner { get; init; } = string.Empty;

    public string Sharing { get; init; } = string.Empty;

    public string Sort { get; init; } = CatalogSortOptions.Relevance;

    public string Cursor { get; init; } = string.Empty;

    public IReadOnlyDictionary<string, string> ToSdkParameters()
    {
        var parameters = new Dictionary<string, string>(StringComparer.Ordinal);
        AddIfPresent(parameters, "q", Query);
        AddIfPresent(parameters, "type", Type);
        AddIfPresent(parameters, "tag", Tag);
        AddIfPresent(parameters, "owner", Owner);
        AddIfPresent(parameters, "sharing", Sharing);
        if (!string.Equals(Sort, CatalogSortOptions.Relevance, StringComparison.Ordinal))
        {
            AddIfPresent(parameters, "sort", Sort);
        }

        AddIfPresent(parameters, "cursor", Cursor);
        return parameters;
    }

    private static void AddIfPresent(IDictionary<string, string> parameters, string key, string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            parameters[key] = value;
        }
    }
}

public static class ConsoleUrlQuery
{
    public static IReadOnlyDictionary<string, string> Parse(string? queryOrFragment)
    {
        if (string.IsNullOrWhiteSpace(queryOrFragment))
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        var query = queryOrFragment.TrimStart('?', '#');
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var segment in query.Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var equalsIndex = segment.IndexOf('=', StringComparison.Ordinal);
            var key = Decode(equalsIndex < 0 ? segment : segment[..equalsIndex]);
            if (string.IsNullOrWhiteSpace(key) || result.ContainsKey(key))
            {
                continue;
            }

            var value = equalsIndex < 0 ? string.Empty : Decode(segment[(equalsIndex + 1)..]);
            result[key] = value;
        }

        return result;
    }

    public static string? GetValue(string uri, string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uri);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var parsed = new Uri(uri, UriKind.Absolute);
        return Parse(parsed.Query).GetValueOrDefault(key);
    }

    public static string ToQueryString(IReadOnlyDictionary<string, string> parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        var encoded = parameters
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
            .Select(pair => $"{Encode(pair.Key)}={Encode(pair.Value)}")
            .ToArray();

        return encoded.Length == 0 ? string.Empty : $"?{string.Join("&", encoded)}";
    }

    private static string Decode(string value) => Uri.UnescapeDataString(value.Replace("+", " ", StringComparison.Ordinal));

    private static string Encode(string value) => Uri.EscapeDataString(value);
}

public sealed record ConsoleContentSummary
{
    public string Id { get; init; } = string.Empty;

    public string Slug { get; init; } = string.Empty;

    public string Type { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;

    public string Summary { get; init; } = string.Empty;

    public string Owner { get; init; } = string.Empty;

    public string[] Tags { get; init; } = [];

    public string[] Formats { get; init; } = [];

    public DateTimeOffset Modified { get; init; } = DateTimeOffset.UtcNow;

    public ConsoleShareAccess Access { get; init; } = new();

    public ConsoleViewerSupport ViewerSupport { get; init; } = new();

    public string ResolvedRole { get; init; } = "viewer";
}

public sealed record ConsoleShareAccess
{
    public string Sharing { get; init; } = CatalogSharingTiers.Private;

    public bool OpenData { get; init; }

    public bool Embeddable { get; init; }

    public string PublicLinkToken { get; init; } = string.Empty;

    public string EmbedToken { get; init; } = string.Empty;
}

public sealed record ConsoleViewerSupport
{
    public bool CanOpenInViewer { get; init; }

    public bool CanEditInStudio { get; init; }

    public ConsoleContentSupportState SupportState { get; init; } = ConsoleContentSupportState.Supported;

    public string UnsupportedReason { get; init; } = string.Empty;
}

public enum ConsoleContentSupportState
{
    Supported,
    UnsupportedServiceMetadata,
    UnsupportedPackageBinding
}

public sealed record ConsoleContentDetail
{
    public ConsoleContentSummary Summary { get; init; } = new();

    public string Description { get; init; } = string.Empty;

    public string[] Capabilities { get; init; } = [];

    public ConsoleContentVersion[] Versions { get; init; } = [];

    public ConsoleContentLineageLink[] Lineage { get; init; } = [];

    public ConsoleContentBinding[] Bindings { get; init; } = [];

    public ConsoleContentPublication[] Publications { get; init; } = [];

    public ConsoleContentPermission[] Permissions { get; init; } = [];

    public ConsoleContentActivity[] Activity { get; init; } = [];

    public ConsoleContentUsage[] Usage { get; init; } = [];
}

public sealed record ConsoleContentVersion(string VersionId, string Label, DateTimeOffset CreatedAt, string CreatedBy);

public sealed record ConsoleContentLineageLink(string Direction, string ItemId, string Title, string Relationship);

public sealed record ConsoleContentBinding(string Name, string Kind, string Target, string Status);

public sealed record ConsoleContentPublication(string Route, string Visibility, bool Embeddable, string Status);

public sealed record ConsoleContentPermission(string Principal, string Role, string Source);

public sealed record ConsoleContentActivity(DateTimeOffset OccurredAt, string Actor, string Event);

public sealed record ConsoleContentUsage(string ConsumerId, string ConsumerTitle, string ConsumerType, string Risk);

public sealed record ConsoleMapPackage
{
    public ConsoleContentSummary Summary { get; init; } = new();

    public string Basemap { get; init; } = "Honua Streets";

    public string[] Layers { get; init; } = [];

    public string InitialExtent { get; init; } = "-158.3,21.1,-157.6,21.8";
}

public sealed record EmbedRouteOptions
{
    public bool Chrome { get; init; } = true;

    public bool Legend { get; init; } = true;

    public bool Zoom { get; init; } = true;

    public string Extent { get; init; } = string.Empty;

    public string EmbedToken { get; init; } = string.Empty;

    public bool QueryStringCarriedToken { get; init; }

    public static EmbedRouteOptions FromUri(string uri) => FromUri(new Uri(uri, UriKind.Absolute));

    public static EmbedRouteOptions FromUri(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);

        var query = ConsoleUrlQuery.Parse(uri.Query);
        var fragment = ConsoleUrlQuery.Parse(uri.Fragment);
        var queryToken = query.ContainsKey("token") || query.ContainsKey("embedToken");

        return new EmbedRouteOptions
        {
            Chrome = ParseBoolean(query.GetValueOrDefault("chrome"), defaultValue: true),
            Legend = ParseBoolean(query.GetValueOrDefault("legend"), defaultValue: true),
            Zoom = ParseBoolean(query.GetValueOrDefault("zoom"), defaultValue: true),
            Extent = NormalizeExtent(query.GetValueOrDefault("extent")),
            EmbedToken = fragment.GetValueOrDefault("embedToken") ?? string.Empty,
            QueryStringCarriedToken = queryToken
        };
    }

    private static bool ParseBoolean(string? value, bool defaultValue)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "1" or "true" or "yes" => true,
            "0" or "false" or "no" => false,
            _ => defaultValue
        };
    }

    private static string NormalizeExtent(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var parts = value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 4)
        {
            return string.Empty;
        }

        foreach (var part in parts)
        {
            if (!double.TryParse(part, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _))
            {
                return string.Empty;
            }
        }

        return string.Join(",", parts);
    }
}
