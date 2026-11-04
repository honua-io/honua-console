namespace Honua.Console.Shell.Models;

public enum StudioLifecyclePhase
{
    Generate,

    Validate,

    Preview,

    Publish,
}

public static class StudioCapabilityIds
{
    public const string Generate = "studio.generate";

    public const string Validate = "studio.validate";

    public const string Preview = "studio.preview";

    public const string Publish = "studio.publish";

    public static string ForPhase(StudioLifecyclePhase phase) => phase switch
    {
        StudioLifecyclePhase.Generate => Generate,
        StudioLifecyclePhase.Validate => Validate,
        StudioLifecyclePhase.Preview => Preview,
        StudioLifecyclePhase.Publish => Publish,
        _ => throw new ArgumentOutOfRangeException(nameof(phase), phase, "Unknown Studio lifecycle phase."),
    };
}

public sealed record StudioIntentResolution : ConsoleOperationResult<StudioIntentResolution>
{
    public string? Lane { get; init; }

    public string? PackageFamilyId { get; init; }

    public StudioLifecyclePhase Phase { get; init; }

    public string? CapabilityId { get; init; }

    public CapabilityDescriptor? Capability { get; init; }

    public bool Hidden { get; init; }

    public static StudioIntentResolution Resolved(
        CapabilityDescriptor capability,
        string? packageFamilyId,
        StudioLifecyclePhase phase) => new()
        {
            Succeeded = true,
            State = "Resolved",
            Lane = "Studio",
            PackageFamilyId = packageFamilyId,
            Phase = phase,
            CapabilityId = capability.Id,
            Capability = capability,
            Hidden = false,
        };

    public static StudioIntentResolution HiddenPhase(
        StudioLifecyclePhase phase,
        string? packageFamilyId,
        string? capabilityId,
        string state,
        string detail) => new()
        {
            Succeeded = false,
            State = state,
            Detail = detail,
            Lane = "Studio",
            PackageFamilyId = packageFamilyId,
            Phase = phase,
            CapabilityId = capabilityId,
            Hidden = true,
        };
}
