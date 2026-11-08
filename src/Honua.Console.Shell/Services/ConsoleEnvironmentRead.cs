using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

internal sealed class ConsoleEnvironmentRead : IDisposable
{
    private readonly IConsoleEnvironmentProfileStore? _profiles;
    private ConsoleEnvironmentProfile? _profile;
    private volatile bool _changed;

    public Uri ServerBaseUri { get; private set; }
    public static OperateCapabilityState Unavailable => new("Services", "Unavailable", "Active Environment",
        "The active environment changed. Reload to read its services.");

    private ConsoleEnvironmentRead(IConsoleEnvironmentProfileStore? profiles, Uri fallback)
    {
        _profiles = profiles;
        ServerBaseUri = fallback;
        if (_profiles is not null) _profiles.ActiveProfileChanged += ProfileChanged;
    }

    public static async Task<ConsoleEnvironmentRead> BeginAsync(
        IConsoleEnvironmentProfileStore? profiles, Uri fallback, CancellationToken cancellationToken)
    {
        var read = new ConsoleEnvironmentRead(profiles, fallback);
        try
        {
            if (profiles is not null)
            {
                read._profile = await profiles.GetActiveProfileAsync(cancellationToken).ConfigureAwait(false);
                read.ServerBaseUri = read._profile?.ServerBaseUri ?? fallback;
            }
            return read;
        }
        catch
        {
            read.Dispose();
            throw;
        }
    }

    public async Task<bool> IsCurrentAsync(CancellationToken cancellationToken)
    {
        if (_profiles is null) return true;
        var current = await _profiles.GetActiveProfileAsync(cancellationToken).ConfigureAwait(false);
        return !_changed && current is not null && current == _profile;
    }

    private void ProfileChanged() => _changed = true;

    public void Dispose()
    {
        if (_profiles is not null) _profiles.ActiveProfileChanged -= ProfileChanged;
    }
}
