using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

public readonly record struct StudioIntentRequest(
    string Prompt,
    string? PackageFamilyId,
    StudioLifecyclePhase Phase);

public interface IStudioIntentResolver
{
    Task<StudioIntentResolution> ResolveAsync(StudioIntentRequest request, CancellationToken cancellationToken = default);
}
