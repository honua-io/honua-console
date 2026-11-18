using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

// Ids and filters can come from a link, and the page context goes into the agent prompt as data.
// A value longer than an identifier, or one with control characters, is left out rather than passed on.
internal static class ConsoleAgentContextBounds
{
    internal const int MaxValueLength = 256;

    // A filter may list selected ids separated by commas; each id gets the identifier bound and the list a total bound.
    internal const int MaxListLength = 64 * (MaxValueLength + 1);

    internal static ConsoleAgentPageContext Apply(ConsoleAgentPageContext context) => context with
    {
        Entities = context.Entities.Where(IsBounded).ToArray(),
        Selection = context.Selection.Where(IsBounded).ToArray(),
        Filters = context.Filters
            .Where(pair => IsBounded(pair.Key) && IsBoundedList(pair.Value))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)
    };

    private static bool IsBounded(ConsoleAgentEntity entity) =>
        IsBounded(entity.Id) && IsBounded(entity.ServiceName) && IsBounded(entity.ResourceId);

    private static bool IsBoundedList(string value) =>
        value.Length <= MaxListLength && value.Split(',').All(IsBounded);

    private static bool IsBounded(string? value) =>
        value is null || (value.Length <= MaxValueLength && !value.Any(char.IsControl));
}
