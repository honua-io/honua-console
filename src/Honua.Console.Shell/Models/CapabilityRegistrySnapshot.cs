namespace Honua.Console.Shell.Models;

/// <summary>
/// Console-side projection of one advertised server capability
/// (<c>Honua.Sdk.Studio.Capabilities.CapabilityEntry</c>, served by
/// <c>GET /api/v1/capabilities/manifest</c>). Carries only the fields the Studio-AI intent resolver
/// needs to gate the generate → validate → preview → publish lifecycle: the stable capability
/// <see cref="Id"/>, whether it is <see cref="Available"/> in the current scope, whether the server
/// <see cref="Supported"/> it at all, and the machine-friendly <see cref="ReasonCode"/> explaining an
/// unavailable-but-supported (deferred) or unsupported state. This is a VIEW model projected from the
/// SDK manifest — never a re-shim of the server wire DTO (Console Patterns Charter §11a).
/// </summary>
/// <param name="Id">Stable capability identifier (e.g. <c>studio.publish</c>).</param>
/// <param name="Available">True when the capability is available in the current tenant/scope.</param>
/// <param name="Supported">True when the server implements the capability at all, even if gated off.</param>
/// <param name="ReasonCode">Machine-friendly reason code explaining the availability state, when known.</param>
public sealed record CapabilityDescriptor(
    string Id,
    bool Available,
    bool Supported,
    string? ReasonCode = null);

/// <summary>
/// A resolved snapshot of the connected deployment's advertised capability descriptors plus its
/// supported package families, projected from the server capability manifest for the registry-driven
/// Studio-AI intent path (honua-console#266). Missing-binding is a first-class state (Console Patterns
/// Charter §11): when no server is bound the <see cref="UnsupportedCapabilityRegistryClient"/> returns
/// <see cref="MissingBinding"/> — an empty snapshot whose <see cref="Bound"/> is false — never fabricated
/// descriptors. The <see cref="IsAvailable"/> / <see cref="IsSupported"/> / <see cref="HasPackageFamily"/>
/// helpers mirror the SDK <c>CapabilityManifest</c> semantics so the resolver gates against the same
/// contract the server advertises.
/// </summary>
public sealed record CapabilityRegistrySnapshot
{
    /// <summary>The advertised capability descriptors. Empty when no server is bound.</summary>
    public IReadOnlyList<CapabilityDescriptor> Descriptors { get; init; } = [];

    /// <summary>Supported package family identifiers (e.g. <c>map</c>, <c>dashboard</c>). Empty when unbound.</summary>
    public IReadOnlyList<string> PackageFamilies { get; init; } = [];

    /// <summary>True when the snapshot was read from a bound server manifest; false for the missing-binding state.</summary>
    public bool Bound { get; init; }

    /// <summary>Neutral state token (e.g. "Resolved", "Missing binding", "Unavailable").</summary>
    public string State { get; init; } = "";

    /// <summary>Optional human-readable detail explaining the snapshot state.</summary>
    public string? Detail { get; init; }

    /// <summary>
    /// The first-class missing-binding snapshot: no descriptors, no families, and an explicit
    /// explanation, returned when no honua-server base URL is configured. The resolver surfaces this as
    /// a <see cref="ConsoleOperationResult{TSelf}.MissingBinding"/> outcome rather than fabricating
    /// availability.
    /// </summary>
    public static CapabilityRegistrySnapshot MissingBinding(string detail) => new()
    {
        Bound = false,
        State = "Missing binding",
        Detail = detail,
    };

    /// <summary>Returns the descriptor with the given id, or null when the snapshot does not advertise it.</summary>
    public CapabilityDescriptor? GetDescriptor(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        for (var i = 0; i < Descriptors.Count; i++)
        {
            if (string.Equals(Descriptors[i].Id, id, StringComparison.Ordinal))
            {
                return Descriptors[i];
            }
        }

        return null;
    }

    /// <summary>True when the snapshot advertises the capability as available in the current scope.</summary>
    public bool IsAvailable(string id) => GetDescriptor(id)?.Available == true;

    /// <summary>True when the server implements the capability at all, even if it is not currently available.</summary>
    public bool IsSupported(string id) => GetDescriptor(id)?.Supported == true;

    /// <summary>True when the snapshot advertises a supported package family with the given identifier.</summary>
    public bool HasPackageFamily(string familyId)
    {
        if (string.IsNullOrWhiteSpace(familyId))
        {
            return false;
        }

        for (var i = 0; i < PackageFamilies.Count; i++)
        {
            if (string.Equals(PackageFamilies[i], familyId, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
