using Honua.Console.Shell.Models;

namespace Honua.Console.Native.Core.Security;

public interface IConsoleAccountTokenProvider
{
    ValueTask<string?> GetAccessTokenAsync(
        ConsoleEnvironmentProfile profile,
        CancellationToken cancellationToken = default);
}
