namespace Honua.Console.Shell.Models;

public sealed record CapabilityDescriptor(
    string Id,
    bool Available,
    bool Supported,
    string? ReasonCode = null);

public sealed record CapabilityRegistrySnapshot
{
    public IReadOnlyList<CapabilityDescriptor> Descriptors { get; init; } = [];

    public IReadOnlyList<string> PackageFamilies { get; init; } = [];

    public bool Bound { get; init; }

    public string State { get; init; } = "";

    public string? Detail { get; init; }

    public static CapabilityRegistrySnapshot MissingBinding(string detail) => new()
    {
        Bound = false,
        State = "Missing binding",
        Detail = detail,
    };

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

    public bool IsAvailable(string id) => GetDescriptor(id)?.Available == true;

    public bool IsSupported(string id) => GetDescriptor(id)?.Supported == true;

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
