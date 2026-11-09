namespace Honua.Console.Shell.Models;

internal static class ConsoleIdentitySet
{
    public static bool IsValid(IEnumerable<string> identities)
    {
        var ids = identities.ToArray();
        return ids.All(id => !string.IsNullOrWhiteSpace(id))
            && ids.Distinct(StringComparer.Ordinal).Count() == ids.Length;
    }
}
