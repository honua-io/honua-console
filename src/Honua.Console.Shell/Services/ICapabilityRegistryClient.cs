using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Reads the connected deployment's advertised capability descriptors + supported package families so the
/// registry-driven Studio-AI intent resolver (honua-console#266) can gate the generate → validate →
/// preview → publish lifecycle on what the server actually supports for the caller's scope. Bound to the
/// server capability manifest (<c>GET /api/v1/capabilities/manifest</c>) through the shared
/// <c>Honua.Sdk.Studio</c> projection (Console Patterns Charter §11a: binding is allowed because the
/// server contract now lives in the SDK). There is no standing in-memory registry in the merged build;
/// when no server is bound the <see cref="UnsupportedCapabilityRegistryClient"/> returns an explicit
/// missing-binding snapshot rather than fabricating availability (Charter §11).
/// </summary>
public interface ICapabilityRegistryClient
{
    /// <summary>
    /// Reads the current capability snapshot. A transport/binding failure is projected into the snapshot's
    /// missing-binding/unavailable state rather than thrown, so callers render an honest explanation.
    /// </summary>
    Task<CapabilityRegistrySnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default);
}
