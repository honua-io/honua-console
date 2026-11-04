using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

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
