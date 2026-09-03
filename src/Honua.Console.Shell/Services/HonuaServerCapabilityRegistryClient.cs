using Honua.Console.Shell.Models;
using Honua.Sdk.Studio.Capabilities;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Live <see cref="ICapabilityRegistryClient"/> bound to the server capability manifest
/// (<c>GET /api/v1/capabilities/manifest</c>) through the shared <c>Honua.Sdk.Studio</c>
/// <see cref="IHonuaCapabilityManifestClient"/> projection (honua-console#266). Projects the SDK
/// <see cref="CapabilityManifest"/> into the console-side <see cref="CapabilityRegistrySnapshot"/> view
/// model — never re-shimming the server wire DTO (Console Patterns Charter §11a). A manifest read failure
/// (no environment bound, endpoint unreachable/forbidden, unsupported) is surfaced as an unavailable
/// snapshot with an honest explanation rather than a fabricated one or an unhandled exception.
/// </summary>
public sealed class HonuaServerCapabilityRegistryClient : ICapabilityRegistryClient
{
    internal const string Contract = "GET /api/v1/capabilities/manifest";

    private readonly IHonuaCapabilityManifestClient _manifestClient;

    public HonuaServerCapabilityRegistryClient(IHonuaCapabilityManifestClient manifestClient)
    {
        _manifestClient = manifestClient ?? throw new ArgumentNullException(nameof(manifestClient));
    }

    public async Task<CapabilityRegistrySnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        CapabilityManifest manifest;
        try
        {
            manifest = await _manifestClient.GetManifestAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // The manifest could not be read (unreachable/forbidden/unsupported). Surface an honest
            // unavailable snapshot — never fabricate availability so an intent slips through the gate.
            return new CapabilityRegistrySnapshot
            {
                Bound = false,
                State = "Unavailable",
                Detail = $"The server capability manifest ({Contract}) could not be read: {ex.Message}",
            };
        }

        return Project(manifest);
    }

    // Projects the SDK manifest into the console view model: each advertised capability entry becomes a
    // descriptor, and each SUPPORTED package family id is carried so the resolver can gate deferred
    // families. Only supported families are surfaced — an unsupported family is treated as absent.
    private static CapabilityRegistrySnapshot Project(CapabilityManifest manifest)
    {
        var descriptors = new List<CapabilityDescriptor>(manifest.Capabilities.Count);
        foreach (var entry in manifest.Capabilities)
        {
            descriptors.Add(new CapabilityDescriptor(
                Id: entry.Id,
                Available: entry.Available,
                Supported: entry.Supported,
                ReasonCode: entry.ReasonCode));
        }

        var families = new List<string>();
        foreach (var family in manifest.Packages?.Families ?? [])
        {
            if (family.Supported && !string.IsNullOrWhiteSpace(family.Id))
            {
                families.Add(family.Id);
            }
        }

        return new CapabilityRegistrySnapshot
        {
            Descriptors = descriptors,
            PackageFamilies = families,
            Bound = true,
            State = "Resolved",
            Detail = null,
        };
    }
}
