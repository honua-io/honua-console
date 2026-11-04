using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

// With no server bound, the snapshot is an explicit missing-binding result and not fabricated availability.
public interface ICapabilityRegistryClient
{
    // A transport or binding failure is returned inside the snapshot and is not thrown.
    Task<CapabilityRegistrySnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default);
}
