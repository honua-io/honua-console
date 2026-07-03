using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Missing-binding <see cref="ICapabilityRegistryClient"/> registered when no honua-server base URL is
/// configured (or the registry-driven intent flag is off). Returns the first-class missing-binding
/// snapshot — no descriptors, no families, <see cref="CapabilityRegistrySnapshot.Bound"/> = false — so the
/// Studio-AI intent resolver refuses to gate against fabricated availability and surfaces an honest
/// missing-binding outcome instead (Console Patterns Charter §11). Mirrors the other <c>Unsupported*</c>
/// datasources.
/// </summary>
public sealed class UnsupportedCapabilityRegistryClient : ICapabilityRegistryClient
{
    internal const string Detail =
        "The capability registry binds to the server capability manifest "
        + "(GET /api/v1/capabilities/manifest) through Honua.Sdk.Studio. Configure Honua:Server:BaseUrl "
        + "(or HONUA_SERVER_BASE_URL) and enable Studio:RegistryIntentResolution; Console will not "
        + "fabricate capability availability from a mock.";

    private static readonly CapabilityRegistrySnapshot Snapshot =
        CapabilityRegistrySnapshot.MissingBinding(Detail);

    public Task<CapabilityRegistrySnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Snapshot);
}
