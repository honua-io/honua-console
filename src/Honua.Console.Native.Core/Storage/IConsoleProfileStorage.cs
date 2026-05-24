namespace Honua.Console.Native.Core.Storage;

public interface IConsoleProfileStorage
{
    ValueTask<string?> GetAsync(string key, CancellationToken cancellationToken = default);

    ValueTask SetAsync(string key, string value, CancellationToken cancellationToken = default);

    ValueTask RemoveAsync(string key, CancellationToken cancellationToken = default);
}
