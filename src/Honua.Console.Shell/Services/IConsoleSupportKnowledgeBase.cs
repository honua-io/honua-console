using Honua.Console.Contracts;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Lexical retrieval over the honua-gis-llm support KB
/// (<c>kb/v0.1/support-kb.jsonl</c>, honua-gis-llm#26). Embeddings are a
/// documented upstream stub (<c>embedding: null</c>), so Console ranks matches
/// by lexical overlap over <c>embedding_text</c> / <c>title</c> / <c>symptoms</c>
/// rather than vector similarity. When the embeddings backend lands, this is the
/// single swap point to a vector search (see the implementation's TODO).
/// </summary>
public interface IConsoleSupportKnowledgeBase
{
    /// <summary>True when at least one KB record loaded.</summary>
    bool IsLoaded { get; }

    /// <summary>Total records available for retrieval.</summary>
    int RecordCount { get; }

    /// <summary>
    /// Returns the top KB matches for a free-text symptom query, best first.
    /// Empty when the query is blank, the KB is absent, or nothing scores above
    /// zero (so the page degrades to assistant + form gracefully).
    /// </summary>
    IReadOnlyList<SupportKbMatch> Search(string query, int limit = 3);
}

/// <summary>A scored KB hit. <see cref="Score"/> is a lexical-overlap rank, not a probability.</summary>
public sealed record SupportKbMatch(SupportKbRecord Record, double Score);
