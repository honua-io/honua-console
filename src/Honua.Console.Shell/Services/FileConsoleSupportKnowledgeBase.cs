using System.Text.Json;
using Honua.Console.Contracts;

namespace Honua.Console.Shell.Services;

public sealed class FileConsoleSupportKnowledgeBase : IConsoleSupportKnowledgeBase
{
    private readonly IReadOnlyList<KbEntry> _entries;

    private FileConsoleSupportKnowledgeBase(IReadOnlyList<KbEntry> entries)
    {
        _entries = entries;
    }

    public bool IsLoaded => _entries.Count > 0;

    public int RecordCount => _entries.Count;

    public static FileConsoleSupportKnowledgeBase Load(string? configuredPath = null)
    {
        var path = ResolvePath(configuredPath);
        if (path is null)
        {
            return new FileConsoleSupportKnowledgeBase([]);
        }

        var entries = new List<KbEntry>();
        try
        {
            foreach (var line in File.ReadLines(path))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                SupportKbRecord? record;
                try
                {
                    record = JsonSerializer.Deserialize(line, SupportAssistantJsonContext.Default.SupportKbRecord);
                }
                catch (JsonException)
                {
                    // Tolerate a malformed line rather than failing the whole KB.
                    continue;
                }

                if (record is null || string.IsNullOrWhiteSpace(record.Id))
                {
                    continue;
                }

                entries.Add(new KbEntry(record, BuildTokenIndex(record)));
            }
        }
        catch (IOException)
        {
            return new FileConsoleSupportKnowledgeBase([]);
        }
        catch (UnauthorizedAccessException)
        {
            return new FileConsoleSupportKnowledgeBase([]);
        }

        return new FileConsoleSupportKnowledgeBase(entries);
    }

    public IReadOnlyList<SupportKbMatch> Search(string query, int limit = 3)
    {
        if (_entries.Count == 0 || string.IsNullOrWhiteSpace(query) || limit <= 0)
        {
            return [];
        }

        var queryTokens = Tokenize(query).ToHashSet(StringComparer.Ordinal);
        if (queryTokens.Count == 0)
        {
            return [];
        }

        // Lexical overlap: count distinct query tokens present in the record's
        // index, lightly boosting title hits. Stable, deterministic, no vectors.
        // TODO(embeddings): swap for cosine similarity once embedding != null.
        return _entries
            .Select(entry => new SupportKbMatch(entry.Record, ScoreOverlap(queryTokens, entry)))
            .Where(match => match.Score > 0)
            .OrderByDescending(match => match.Score)
            .ThenBy(match => match.Record.Id, StringComparer.Ordinal)
            .Take(limit)
            .ToArray();
    }

    private static double ScoreOverlap(IReadOnlyCollection<string> queryTokens, KbEntry entry)
    {
        double score = 0;
        foreach (var token in queryTokens)
        {
            if (entry.TitleTokens.Contains(token))
            {
                score += 2.0;
            }
            else if (entry.BodyTokens.Contains(token))
            {
                score += 1.0;
            }
        }

        // Normalize by query length so longer queries do not dominate ranking.
        return score / queryTokens.Count;
    }

    private static string? ResolvePath(string? configuredPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath.Trim()))
        {
            return configuredPath.Trim();
        }

        // Bundled artifact copied next to the Shell assembly via the csproj
        // <Content> item (SupportKb/support-kb.jsonl).
        var assemblyDir = Path.GetDirectoryName(typeof(FileConsoleSupportKnowledgeBase).Assembly.Location);
        if (!string.IsNullOrWhiteSpace(assemblyDir))
        {
            var bundled = Path.Combine(assemblyDir, "SupportKb", "support-kb.jsonl");
            if (File.Exists(bundled))
            {
                return bundled;
            }
        }

        return null;
    }

    private static KbTokenIndex BuildTokenIndex(SupportKbRecord record)
    {
        var titleTokens = Tokenize(record.Title)
            .Concat(record.FaultCode is { } fault ? Tokenize(fault) : [])
            .ToHashSet(StringComparer.Ordinal);

        var bodyText = string.Join(
            ' ',
            new[] { record.EmbeddingText }
                .Concat(record.Symptoms)
                .Concat(record.Remediation)
                .Concat(record.Tags));
        var bodyTokens = Tokenize(bodyText).ToHashSet(StringComparer.Ordinal);

        return new KbTokenIndex(titleTokens, bodyTokens);
    }

    private static IEnumerable<string> Tokenize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            yield break;
        }

        foreach (var raw in text.Split(
            [' ', '\t', '\n', '\r', '.', ',', ';', ':', '(', ')', '[', ']', '/', '\\', '"', '\'', '-', '_'],
            StringSplitOptions.RemoveEmptyEntries))
        {
            var token = raw.ToLowerInvariant();
            // Drop very short noise tokens; keep 3+ char terms and fault-ish codes.
            if (token.Length >= 3)
            {
                yield return token;
            }
        }
    }

    private sealed record KbEntry(SupportKbRecord Record, KbTokenIndex Index)
    {
        public IReadOnlySet<string> TitleTokens => Index.TitleTokens;

        public IReadOnlySet<string> BodyTokens => Index.BodyTokens;
    }

    private sealed record KbTokenIndex(IReadOnlySet<string> TitleTokens, IReadOnlySet<string> BodyTokens);
}
