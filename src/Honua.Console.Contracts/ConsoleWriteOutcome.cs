namespace Honua.Console.Contracts;

public static class ConsoleWriteOutcome
{
    public const string UnverifiedState = "Outcome unverified";
    public const string UnverifiedDetail = "The response was not confirmed. Read the original environment to check the outcome before making another change.";
    public static readonly HttpRequestOptionsKey<bool> Unverified = new("Honua.Console.Authority.OutcomeUnverified");
    public static bool IsUnverified(HttpRequestMessage request) => request.Options.TryGetValue(Unverified, out var unverified) && unverified;
}
