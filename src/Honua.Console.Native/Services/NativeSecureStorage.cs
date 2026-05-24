using Honua.Console.Native.Core.Security;
using Honua.Console.Native.Core.Storage;
using Microsoft.Maui.Storage;

namespace Honua.Console.Native.Services;

public sealed class NativeSecureStorage : IConsoleProfileStorage, INativeSecretStore
{
    public async ValueTask<string?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await SecureStorage.Default.GetAsync(key).ConfigureAwait(false);
    }

    public async ValueTask SetAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await SecureStorage.Default.SetAsync(key, value).ConfigureAwait(false);
    }

    public ValueTask RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SecureStorage.Default.Remove(key);
        return ValueTask.CompletedTask;
    }

    public ValueTask<string?> GetSecretAsync(string key, CancellationToken cancellationToken = default) =>
        GetAsync(key, cancellationToken);

    public ValueTask SetSecretAsync(string key, string value, CancellationToken cancellationToken = default) =>
        SetAsync(key, value, cancellationToken);

    public ValueTask RemoveSecretAsync(string key, CancellationToken cancellationToken = default) =>
        RemoveAsync(key, cancellationToken);
}
