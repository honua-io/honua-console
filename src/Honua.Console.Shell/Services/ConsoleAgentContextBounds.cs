using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

// Ids and filters can come from a link, and the page context goes into the agent prompt as data.
// A value longer than an identifier, or one with control characters, is left out rather than passed on.
internal static class ConsoleAgentContextBounds
{
    internal const int MaxValueLength = 256;

    internal static ConsoleAgentPageContext Apply(ConsoleAgentPageContext context) => context with
    {
        Entities = context.Entities.Where(IsBounded).ToArray(),
        Selection = context.Selection.Where(IsBounded).ToArray(),
        Filters = context.Filters
            .Where(pair => IsBounded(pair.Key) && IsBounded(pair.Value))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)
    };

    private static bool IsBounded(ConsoleAgentEntity entity) =>
        IsBounded(entity.Id) && IsBounded(entity.ServiceName) && IsBounded(entity.ResourceId);

    private static bool IsBounded(string? value) =>
        value is null || (value.Length <= MaxValueLength && !value.Any(char.IsControl));
}
