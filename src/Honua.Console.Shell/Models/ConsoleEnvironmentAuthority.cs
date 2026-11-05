namespace Honua.Console.Shell.Models;

public sealed record ConsoleEnvironmentAuthority(string? ProfileId, Uri? Server, string? AccountId)
{
    public static ConsoleEnvironmentAuthority From(ConsoleEnvironmentProfile? profile) =>
        new(profile?.Id, profile?.ServerBaseUri, profile?.Account.AccountId);
}
