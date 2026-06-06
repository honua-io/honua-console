using Honua.Console.Contracts;

namespace Honua.Console.Shell.Models;

// Console-side projections of the honua-server catalog discovery-endpoints registry (honua-server#1279),
// scoped to the Operate > Catalogs screens (CatalogsList + EsriCatalogDetail + CatalogItemEditor). These are
// immutable view records the Catalogs pages render; they carry no local registry state and never fabricate
// endpoint/item data — when the server is unbound or a read is rejected, the surface renders a capability
// state instead (Console Patterns Charter section 11).

/// <summary>
/// A binding/permission/not-found/missing surface for the Catalogs pages, mirroring the RBAC/Share access
/// capability-state pattern so missing bindings, missing permissions, and unsupported contracts render
/// consistently across the registry, endpoint detail, and item editor surfaces.
/// </summary>
public sealed record CatalogDiscoveryCapabilityState(
    string Surface,
    string State,
    string Contract,
    string Detail);

/// <summary>One feeder that populates a discovery endpoint (kind tag + label).</summary>
public sealed record CatalogFeederView(string Kind, string Label);

/// <summary>One discovery-endpoint card: dialect, on/off, auto-default vs opt-in, feeders, entries, issues.</summary>
public sealed record CatalogEndpointView(
    string Key,
    string Title,
    string? Description,
    string Dialect,
    bool Enabled,
    bool AutoDefault,
    string? Url,
    int Entries,
    string? FedBy,
    IReadOnlyList<CatalogFeederView> Feeders,
    int IssueCount)
{
    /// <summary>True when the endpoint auto-mirrors publications; false when resources opt in per-resource.</summary>
    public bool IsAutoDefault => AutoDefault;

    public bool HasIssues => IssueCount > 0;
}

/// <summary>The CatalogsList projection: the endpoint cards plus the auto-default / opt-in aggregate counts.</summary>
public sealed record CatalogDiscoveryRegistryView(
    string WorkspaceId,
    string? WorkspaceName,
    string? PublicHost,
    IReadOnlyList<CatalogEndpointView> Endpoints,
    int AutoDefaultCount,
    int OptInCount)
{
    public string DisplayWorkspace => string.IsNullOrWhiteSpace(WorkspaceName) ? WorkspaceId : WorkspaceName!;
}

/// <summary>One row in the EsriCatalogDetail items table.</summary>
public sealed record CatalogEndpointItemView(
    string Id,
    string Title,
    string? FromService,
    string? Resource,
    IReadOnlyList<string> Tags,
    bool HasThumbnail,
    string? License,
    int IssueCount,
    string? Updated)
{
    public bool HasIssues => IssueCount > 0;
}

/// <summary>The EsriCatalogDetail projection: the endpoint header plus its mirrored items table.</summary>
public sealed record CatalogEndpointDetailView(
    CatalogEndpointView Endpoint,
    string? LastRebuild,
    bool AutoMirror,
    IReadOnlyList<CatalogEndpointItemView> Items);

/// <summary>One field in the CatalogItemEditor with its source state and (for derived fields) its origin.</summary>
public sealed record CatalogItemFieldView(
    string Label,
    string State,
    string? Value,
    string? DerivedFrom)
{
    public bool IsCalculated => string.Equals(State, HonuaCatalogFieldStates.Calculated, StringComparison.Ordinal);

    public bool IsSystem => string.Equals(State, HonuaCatalogFieldStates.System, StringComparison.Ordinal);

    public bool IsInput => string.Equals(State, HonuaCatalogFieldStates.Input, StringComparison.Ordinal);
}

/// <summary>One field group in the CatalogItemEditor (Identity, Catalog presentation, Service bindings, Standards).</summary>
public sealed record CatalogItemFieldGroupView(
    string Title,
    string? Subtitle,
    string? Scope,
    IReadOnlyList<CatalogItemFieldView> Fields);

/// <summary>The CatalogItemEditor projection: an auto-mirrored item with its field groups.</summary>
public sealed record CatalogItemView(
    string Id,
    string Title,
    bool AutoMirror,
    bool Live,
    int BackingServiceCount,
    int TagCount,
    int IssueCount,
    IReadOnlyList<CatalogItemFieldGroupView> Groups)
{
    public bool HasIssues => IssueCount > 0;
}

/// <summary>Result of loading the discovery registry: the view, or the capability states explaining why not.</summary>
public sealed record CatalogDiscoveryRegistryLoad(
    CatalogDiscoveryRegistryView? Registry,
    IReadOnlyList<CatalogDiscoveryCapabilityState> CapabilityStates)
{
    public bool HasRegistry => Registry is not null;
}

/// <summary>Result of loading one endpoint detail: the view, or the capability states explaining why not.</summary>
public sealed record CatalogEndpointDetailLoad(
    CatalogEndpointDetailView? Detail,
    IReadOnlyList<CatalogDiscoveryCapabilityState> CapabilityStates)
{
    public bool HasDetail => Detail is not null;
}

/// <summary>Result of loading one catalog item: the view, or the capability states explaining why not.</summary>
public sealed record CatalogItemLoad(
    CatalogItemView? Item,
    IReadOnlyList<CatalogDiscoveryCapabilityState> CapabilityStates)
{
    public bool HasItem => Item is not null;
}

/// <summary>Maps the honua-server catalog discovery wire records to the Catalogs page view records.</summary>
public static class CatalogDiscoveryMapper
{
    public static CatalogEndpointView ToView(HonuaCatalogEndpoint endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        return new CatalogEndpointView(
            Key: endpoint.Key,
            Title: endpoint.Title,
            Description: endpoint.Description,
            Dialect: endpoint.Dialect,
            Enabled: endpoint.Enabled,
            AutoDefault: endpoint.AutoDefault,
            Url: endpoint.Url,
            Entries: endpoint.Entries,
            FedBy: endpoint.FedBy,
            Feeders: (endpoint.Feeders ?? [])
                .Select(feeder => new CatalogFeederView(feeder.Kind, feeder.Label))
                .ToArray(),
            IssueCount: endpoint.IssueCount);
    }

    public static CatalogDiscoveryRegistryView ToView(HonuaCatalogDiscoveryRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);

        return new CatalogDiscoveryRegistryView(
            WorkspaceId: registry.WorkspaceId,
            WorkspaceName: registry.WorkspaceName,
            PublicHost: registry.PublicHost,
            Endpoints: (registry.Endpoints ?? []).Select(ToView).ToArray(),
            AutoDefaultCount: registry.AutoDefaultCount,
            OptInCount: registry.OptInCount);
    }

    public static CatalogEndpointDetailView ToView(HonuaCatalogEndpointDetail detail)
    {
        ArgumentNullException.ThrowIfNull(detail);

        return new CatalogEndpointDetailView(
            Endpoint: ToView(detail.Endpoint),
            LastRebuild: detail.LastRebuild,
            AutoMirror: detail.AutoMirror,
            Items: (detail.Items ?? [])
                .Select(item => new CatalogEndpointItemView(
                    item.Id,
                    item.Title,
                    item.FromService,
                    item.Resource,
                    item.Tags ?? [],
                    item.HasThumbnail,
                    item.License,
                    item.IssueCount,
                    item.Updated))
                .ToArray());
    }

    public static CatalogItemView ToView(HonuaCatalogItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        return new CatalogItemView(
            Id: item.Id,
            Title: item.Title,
            AutoMirror: item.AutoMirror,
            Live: item.Live,
            BackingServiceCount: item.BackingServiceCount,
            TagCount: item.TagCount,
            IssueCount: item.IssueCount,
            Groups: (item.Groups ?? [])
                .Select(group => new CatalogItemFieldGroupView(
                    group.Title,
                    group.Subtitle,
                    group.Scope,
                    (group.Fields ?? [])
                        .Select(field => new CatalogItemFieldView(field.Label, field.State, field.Value, field.DerivedFrom))
                        .ToArray()))
                .ToArray());
    }
}
