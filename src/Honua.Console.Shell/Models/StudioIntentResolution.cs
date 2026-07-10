namespace Honua.Console.Shell.Models;

/// <summary>
/// A phase of the Studio-AI authoring lifecycle the intent resolver gates against the capability
/// registry (honua-console#266). Each phase maps to a stable capability id
/// (<see cref="StudioCapabilityIds"/>); a phase whose capability is unavailable — or whose target
/// package family is deferred — is hidden from Studio AI rather than offered and failing later.
/// </summary>
public enum StudioLifecyclePhase
{
    /// <summary>NL → package generation (grounding + validation + repair loop on the server).</summary>
    Generate,

    /// <summary>Package validation against the server package schema/rules.</summary>
    Validate,

    /// <summary>Non-destructive preview of the generated/validated package.</summary>
    Preview,

    /// <summary>Publishing the package to the server content/publication registry.</summary>
    Publish,
}

/// <summary>
/// Stable capability identifiers for the four Studio-AI lifecycle phases advertised on the server
/// capability manifest. The resolver checks <c>IsAvailable</c> for the phase's id AND that the target
/// package family is advertised (<c>HasPackageFamily</c>) before exposing the phase to Studio AI.
/// </summary>
public static class StudioCapabilityIds
{
    /// <summary>Capability id gating the generate phase.</summary>
    public const string Generate = "studio.generate";

    /// <summary>Capability id gating the validate phase.</summary>
    public const string Validate = "studio.validate";

    /// <summary>Capability id gating the preview phase.</summary>
    public const string Preview = "studio.preview";

    /// <summary>Capability id gating the publish phase.</summary>
    public const string Publish = "studio.publish";

    /// <summary>Maps a lifecycle phase to its stable capability id.</summary>
    public static string ForPhase(StudioLifecyclePhase phase) => phase switch
    {
        StudioLifecyclePhase.Generate => Generate,
        StudioLifecyclePhase.Validate => Validate,
        StudioLifecyclePhase.Preview => Preview,
        StudioLifecyclePhase.Publish => Publish,
        _ => throw new ArgumentOutOfRangeException(nameof(phase), phase, "Unknown Studio lifecycle phase."),
    };
}

/// <summary>
/// The outcome of resolving a Studio-AI intent (a free-text prompt + target package family + lifecycle
/// phase) against the capability registry snapshot (honua-console#266). Follows the shared operation-result
/// vocabulary (Console Patterns Charter §6a): <see cref="ConsoleOperationResult{TSelf}.Succeeded"/> /
/// <see cref="ConsoleOperationResult{TSelf}.State"/> / <see cref="ConsoleOperationResult{TSelf}.Detail"/>
/// plus the inherited <see cref="ConsoleOperationResult{TSelf}.MissingBinding"/> factory. A resolved intent
/// carries the concrete <see cref="Capability"/> descriptor; a deferred/unavailable capability yields
/// <see cref="Hidden"/> = true and a non-success state so Studio AI hides the phase instead of throwing.
/// </summary>
public sealed record StudioIntentResolution : ConsoleOperationResult<StudioIntentResolution>
{
    /// <summary>The classifier lane the prompt routed to (Studio for a resolvable authoring intent).</summary>
    public string? Lane { get; init; }

    /// <summary>The target package family the intent addresses (e.g. <c>map</c>), when supplied.</summary>
    public string? PackageFamilyId { get; init; }

    /// <summary>The lifecycle phase being resolved.</summary>
    public StudioLifecyclePhase Phase { get; init; }

    /// <summary>The capability id the phase maps to.</summary>
    public string? CapabilityId { get; init; }

    /// <summary>The resolved capability descriptor when the phase is available; null when hidden or unbound.</summary>
    public CapabilityDescriptor? Capability { get; init; }

    /// <summary>
    /// True when the phase must be HIDDEN from Studio AI because its capability is unavailable or its
    /// package family is deferred. A hidden result is not an error to surface loudly — it is the honest
    /// "not offered in this deployment" state that keeps the affordance out of the UI.
    /// </summary>
    public bool Hidden { get; init; }

    /// <summary>An available, registry-backed phase resolved to its capability descriptor.</summary>
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

    /// <summary>
    /// A phase hidden from Studio AI: either the capability is unavailable/unsupported, or the target
    /// package family is deferred. Non-success + <see cref="Hidden"/> so callers gate the affordance out.
    /// </summary>
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
