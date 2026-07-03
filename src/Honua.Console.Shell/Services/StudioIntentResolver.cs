using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Registry-backed <see cref="IStudioIntentResolver"/> (honua-console#266): gates each Studio-AI lifecycle
/// phase on the live capability manifest snapshot so deferred/unavailable capabilities are HIDDEN from
/// Studio AI. Registered only when the <c>Studio:RegistryIntentResolution</c> flag is ON and a server base
/// URL is configured; otherwise the <see cref="NoopStudioIntentResolver"/> preserves current behavior.
///
/// Resolution order:
/// <list type="number">
///   <item>Classify the prompt; a HIGH-confidence DevOps verdict is out of the Studio lane, so it is
///   hidden with an honest "not a Studio authoring intent" explanation (never misrouted).</item>
///   <item>Read the registry snapshot; an unbound snapshot yields the shared missing-binding outcome.</item>
///   <item>If a target package family is supplied and the manifest does not advertise it
///   (<c>HasPackageFamily</c> false), the phase is hidden — the family is deferred.</item>
///   <item>If the phase's capability is not available (<c>IsAvailable</c> false), the phase is hidden;
///   the state distinguishes a supported-but-gated (deferred) capability from an unsupported one.</item>
///   <item>Otherwise the phase resolves to its capability descriptor.</item>
/// </list>
/// Every path returns a <see cref="StudioIntentResolution"/> — no exceptions escape the gate.
/// </summary>
public sealed class StudioIntentResolver : IStudioIntentResolver
{
    private readonly IOmniPromptIntentClassifier _classifier;
    private readonly ICapabilityRegistryClient _registry;

    public StudioIntentResolver(IOmniPromptIntentClassifier classifier, ICapabilityRegistryClient registry)
    {
        _classifier = classifier ?? throw new ArgumentNullException(nameof(classifier));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    public async Task<StudioIntentResolution> ResolveAsync(
        StudioIntentRequest request,
        CancellationToken cancellationToken = default)
    {
        var capabilityId = StudioCapabilityIds.ForPhase(request.Phase);

        // A confident DevOps verdict is not a Studio authoring intent; hide the phase rather than gating an
        // ops prompt against Studio capabilities. Ambiguous/low-confidence prompts default to Studio (the
        // same default the omni-prompt page uses) and proceed to the registry gate.
        var classification = _classifier.Classify(request.Prompt);
        if (classification is { Intent: OmniPromptIntent.DevOps, Confidence: OmniPromptConfidence.High })
        {
            return StudioIntentResolution.HiddenPhase(
                request.Phase,
                request.PackageFamilyId,
                capabilityId,
                state: "Rejected",
                detail: "This reads as an ops action, not a Studio authoring intent, so the Studio "
                    + "generate/validate/preview/publish lifecycle does not apply.");
        }

        var snapshot = await _registry.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
        if (!snapshot.Bound)
        {
            // Preserve the missing-binding / unavailable explanation from the snapshot.
            return snapshot.State == "Missing binding"
                ? StudioIntentResolution.MissingBinding(
                    snapshot.Detail ?? "The capability registry is not bound to a server.")
                : StudioIntentResolution.HiddenPhase(
                    request.Phase,
                    request.PackageFamilyId,
                    capabilityId,
                    state: string.IsNullOrEmpty(snapshot.State) ? "Unavailable" : snapshot.State,
                    detail: snapshot.Detail ?? "The capability registry snapshot is unavailable.");
        }

        // A deferred package family (advertised as unsupported / absent) hides every phase for that family.
        if (!string.IsNullOrWhiteSpace(request.PackageFamilyId)
            && !snapshot.HasPackageFamily(request.PackageFamilyId))
        {
            return StudioIntentResolution.HiddenPhase(
                request.Phase,
                request.PackageFamilyId,
                capabilityId,
                state: "Unavailable",
                detail: $"The '{request.PackageFamilyId}' package family is not advertised by this "
                    + "deployment (deferred), so its Studio authoring lifecycle is hidden.");
        }

        var descriptor = snapshot.GetDescriptor(capabilityId);
        if (descriptor is null || !descriptor.Available)
        {
            // Distinguish a supported-but-gated capability (deferred: light up when entitled/opted-in) from
            // one the server does not implement at all.
            var supported = descriptor?.Supported == true;
            var reason = descriptor?.ReasonCode is { Length: > 0 } code ? $" ({code})" : string.Empty;
            return StudioIntentResolution.HiddenPhase(
                request.Phase,
                request.PackageFamilyId,
                capabilityId,
                state: supported ? "Unavailable" : "Unsupported",
                detail: supported
                    ? $"The '{capabilityId}' capability is supported but not available in this scope{reason}; "
                        + $"the {request.Phase} phase is hidden until it is enabled."
                    : $"The '{capabilityId}' capability is not supported by this deployment{reason}; "
                        + $"the {request.Phase} phase is hidden.");
        }

        return StudioIntentResolution.Resolved(descriptor, request.PackageFamilyId, request.Phase);
    }
}

/// <summary>
/// No-op <see cref="IStudioIntentResolver"/> registered when the <c>Studio:RegistryIntentResolution</c>
/// flag is OFF (the default). It preserves the CURRENT behavior: every Studio-AI lifecycle phase resolves
/// as available WITHOUT registry gating, so the generate/validate/preview/publish affordances are offered
/// exactly as they are today. It still delegates the lane annotation to the existing classifier (so the
/// resolved result carries the same Studio-vs-DevOps rationale), but never hides a phase. This is the
/// default so opting into registry-driven resolution changes nothing unless explicitly enabled.
/// </summary>
public sealed class NoopStudioIntentResolver : IStudioIntentResolver
{
    private readonly IOmniPromptIntentClassifier _classifier;

    public NoopStudioIntentResolver(IOmniPromptIntentClassifier classifier)
    {
        _classifier = classifier ?? throw new ArgumentNullException(nameof(classifier));
    }

    public Task<StudioIntentResolution> ResolveAsync(
        StudioIntentRequest request,
        CancellationToken cancellationToken = default)
    {
        // No registry gating: resolve the phase as available so behavior is unchanged from the pre-flag
        // path. The classifier is consulted only to annotate the lane, never to hide the phase.
        _ = _classifier.Classify(request.Prompt);
        var capabilityId = StudioCapabilityIds.ForPhase(request.Phase);
        var descriptor = new CapabilityDescriptor(capabilityId, Available: true, Supported: true);
        return Task.FromResult(
            StudioIntentResolution.Resolved(descriptor, request.PackageFamilyId, request.Phase));
    }
}
