using Honua.Console.Contracts;

namespace Honua.Console.Shell.Services;

public interface IConsoleSupportKnowledgeBase
{
    bool IsLoaded { get; }

    int RecordCount { get; }

    IReadOnlyList<SupportKbMatch> Search(string query, int limit = 3);
}

public sealed record SupportKbMatch(SupportKbRecord Record, double Score);
