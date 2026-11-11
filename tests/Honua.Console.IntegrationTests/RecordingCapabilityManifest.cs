using Honua.Console.Shell.Services;

namespace Honua.Console.IntegrationTests;

internal sealed class RecordingCapabilityManifest : IConsoleCapabilityManifest
{
    public int RefreshCount { get; private set; }

    public Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        RefreshCount++;
        return Task.CompletedTask;
    }

    public bool IsAdvertised(string capabilityKey) => false;
}
