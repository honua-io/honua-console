namespace Honua.Console.Shell.Models;

// Data-source-facing projections for the Studio spatial query builder (/studio/query, honua-console#52).
//
// These mirror the server-bound editor seam already established for the form builder (#57) and Operate
// transition surfaces: a workspace projection, a per-package list item, and a capability-state record so
// missing bindings, missing permissions, and unsupported contracts render through one shared surface.
//
// The query builder binds to the server-owned saved query content/version lifecycle landed in
// honua-server#1182 ("Saved query and analysis content versions with job artifacts"). Per the Console
// Patterns Charter section 11 there is no standing in-memory query client in the merged result: when no
// honua-server base address is configured the unsupported data source returns an explicit missing-binding
// capability state instead of fabricating query packages.

/// <summary>
/// A binding/permission/empty surface for the query builder, mirroring the form-builder and Operate
/// capability-state pattern so missing bindings, missing permissions, and unsupported contracts render
/// consistently across Studio editors.
/// </summary>
public sealed record StudioQueryCapabilityState(
    string Surface,
    string State,
    string Contract,
    string Detail);

/// <summary>
/// The query builder workspace: the server's saved query packages plus any binding/permission capability
/// states. An empty package list with no capability states is a real (bound) empty workspace; a
/// missing-binding capability state means no server is configured and the surface is blocked.
/// </summary>
public sealed record StudioQueryWorkspace(
    IReadOnlyList<StudioQueryPackageListItem> Packages,
    IReadOnlyList<StudioQueryCapabilityState> CapabilityStates);

/// <summary>One saved query package as listed in the builder workspace.</summary>
public sealed record StudioQueryPackageListItem(
    string QueryId,
    string Title,
    string SourceBinding,
    int? DraftVersion,
    int? PublishedVersion,
    DateTimeOffset UpdatedAt);
