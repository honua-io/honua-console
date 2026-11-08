using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

public enum ConsoleManagementBindingStatus
{
    ServingOnly,
    Separate,
    Unavailable
}

public sealed record ConsoleManagementBinding(ConsoleManagementBindingStatus Status, Uri? BaseUri)
{
    public static ConsoleManagementBinding Resolve(IConsoleManagerEndpoint? endpoint, Uri servingBaseUri)
    {
        if (endpoint is null) return new(ConsoleManagementBindingStatus.ServingOnly, null);
        var target = endpoint.Resolve(servingBaseUri);
        if (target is null) return new(ConsoleManagementBindingStatus.Unavailable, null);
        return new Uri(target.AbsoluteUri.TrimEnd('/') + "/") == new Uri(servingBaseUri.AbsoluteUri.TrimEnd('/') + "/")
            ? new(ConsoleManagementBindingStatus.ServingOnly, null)
            : new(ConsoleManagementBindingStatus.Separate, target);
    }

    public ConsoleEnvironmentProfile? ForAuthority(ConsoleEnvironmentProfile profile, ConsoleServerAuthority authority) => authority switch
    {
        ConsoleServerAuthority.Serving => profile,
        ConsoleServerAuthority.Management when Status == ConsoleManagementBindingStatus.Separate => profile with { ServerBaseUri = BaseUri! },
        _ => null
    };
}
