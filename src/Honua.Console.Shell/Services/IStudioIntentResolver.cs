using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// A request to resolve one Studio-AI lifecycle phase against the capability registry (honua-console#266):
/// the free-text <see cref="Prompt"/> (classified to confirm the Studio lane), the target
/// <see cref="PackageFamilyId"/> the authoring intent addresses (e.g. <c>map</c>; optional), and the
/// <see cref="Phase"/> of the generate → validate → preview → publish lifecycle being resolved.
/// </summary>
/// <param name="Prompt">The author's free-text prompt.</param>
/// <param name="PackageFamilyId">The target package family, when known; gated via <c>HasPackageFamily</c>.</param>
/// <param name="Phase">The lifecycle phase to resolve.</param>
public readonly record struct StudioIntentRequest(
    string Prompt,
    string? PackageFamilyId,
    StudioLifecyclePhase Phase);

/// <summary>
/// Resolves a Studio-AI authoring intent (a prompt + target package family + lifecycle phase) against the
/// <see cref="ICapabilityRegistryClient"/> snapshot so the generate → validate → preview → publish path
/// honors manifest-driven availability (honua-console#266). It returns the resolved capability descriptor
/// for an available phase, and HIDES a phase whose capability is unavailable/unsupported or whose package
/// family is deferred — surfaced through the shared operation-result vocabulary
/// (<see cref="StudioIntentResolution"/>: missing-binding / non-success + hidden), NEVER an exception.
///
/// Two implementations are registered behind the <c>Studio:RegistryIntentResolution</c> flag: the
/// registry-backed <see cref="StudioIntentResolver"/> (flag ON + a bound server) and the
/// <see cref="NoopStudioIntentResolver"/> (flag OFF) that preserves the current behavior by resolving every
/// phase as available without registry gating.
/// </summary>
public interface IStudioIntentResolver
{
    Task<StudioIntentResolution> ResolveAsync(StudioIntentRequest request, CancellationToken cancellationToken = default);
}
