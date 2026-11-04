using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Validation;

public static class TemporalDiffFieldKeys
{
    public const string From = "temporal.diff.from";
    public const string To = "temporal.diff.to";
}

public sealed record TemporalDiffSelection(
    string? FromCheckpointId,
    string? ToCheckpointId,
    IReadOnlyList<TemporalCheckpoint> Checkpoints);

public sealed class TemporalDiffSelectionValidator : IFieldValidator<TemporalDiffSelection>
{
    public static TemporalDiffSelectionValidator Instance { get; } = new();

    public IReadOnlyList<ConsoleFieldError> Evaluate(TemporalDiffSelection state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var errors = new List<ConsoleFieldError>();

        var fromIndex = IndexOf(state, state.FromCheckpointId);
        var toIndex = IndexOf(state, state.ToCheckpointId);

        if (string.IsNullOrWhiteSpace(state.FromCheckpointId))
        {
            errors.Add(Blocker(TemporalDiffFieldKeys.From, "temporal.diff.from.required", "Choose an as-of (from) checkpoint."));
        }

        if (string.IsNullOrWhiteSpace(state.ToCheckpointId))
        {
            errors.Add(Blocker(TemporalDiffFieldKeys.To, "temporal.diff.to.required", "Choose a now (to) checkpoint."));
        }

        // Ordering: the as-of (from) checkpoint must be at or before the now (to) checkpoint. Checkpoints are
        // listed newest-first, so "at or before" means the from checkpoint's list index is >= the to index.
        // Only checked when both ids resolve to a known checkpoint position.
        if (fromIndex >= 0 && toIndex >= 0 && fromIndex < toIndex)
        {
            errors.Add(Error(
                TemporalDiffFieldKeys.From,
                "temporal.diff.order",
                "The as-of (from) checkpoint must be at or before the now (to) checkpoint."));
        }

        return errors;
    }

    private static int IndexOf(TemporalDiffSelection state, string? checkpointId)
    {
        if (string.IsNullOrWhiteSpace(checkpointId))
        {
            return -1;
        }

        for (var i = 0; i < state.Checkpoints.Count; i++)
        {
            if (string.Equals(state.Checkpoints[i].CheckpointId, checkpointId, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private static ConsoleFieldError Blocker(string key, string code, string message) =>
        new(key, code, ConsoleValidationSeverity.Blocker, message);

    private static ConsoleFieldError Error(string key, string code, string message) =>
        new(key, code, ConsoleValidationSeverity.Error, message);
}
