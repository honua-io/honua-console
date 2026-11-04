using Honua.Console.Contracts;

namespace Honua.Console.Shell.Services;

// Records carry a null embedding, so search ranks lexical overlap over the text fields.
public interface IConsoleSupportKnowledgeBase
{
    bool IsLoaded { get; }

    int RecordCount { get; }

    IReadOnlyList<SupportKbMatch> Search(string query, int limit = 3);
}

// Score is a lexical-overlap rank, not a probability.
public sealed record SupportKbMatch(SupportKbRecord Record, double Score);
