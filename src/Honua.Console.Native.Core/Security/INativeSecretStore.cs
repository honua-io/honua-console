namespace Honua.Console.Native.Core.Security;

public interface INativeSecretStore
{
    ValueTask<string?> GetSecretAsync(string key, CancellationToken cancellationToken = default);

    ValueTask SetSecretAsync(string key, string value, CancellationToken cancellationToken = default);

    ValueTask RemoveSecretAsync(string key, CancellationToken cancellationToken = default);
}
