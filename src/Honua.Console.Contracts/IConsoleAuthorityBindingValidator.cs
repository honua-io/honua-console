namespace Honua.Console.Contracts;

// Host-only request metadata. Never serialized and never taken from browser headers.
public interface IConsoleAuthorityBindingValidator
{
    Task<bool> IsCurrentAsync(string binding, CancellationToken cancellationToken = default);
    Task<bool> RevalidateAsync(string binding, CancellationToken cancellationToken = default) => IsCurrentAsync(binding, cancellationToken);
    Task<string?> CaptureAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
}
