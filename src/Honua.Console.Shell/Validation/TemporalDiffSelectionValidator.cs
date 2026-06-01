using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Validation;

/// <summary>
/// Stable console-owned field keys for the Operate temporal diff selection (<c>OperateTemporalPage</c>).
/// </summary>
public static class TemporalDiffFieldKeys
{
    public const string From = "temporal.diff.from";
    public const string To = "temporal.diff.to";
}

/// <summary>
/// Console-owned snapshot of the temporal diff selection: the chosen from (as-of) and to (now) checkpoint ids
/// plus the available checkpoints, ordered newest-first as the page lists them. The live diff/rollback data stays
/// missing-binding; this validator only gates the <em>selection</em> before the diff action runs.
/// </summary>
/// <param name="FromCheckpointId">The selected as-of (from) checkpoint id.</param>
/// <param name="ToCheckpointId">The selected now (to) checkpoint id.</param>
/// <param name="Checkpoints">
/// The available checkpoints in the page's display order (index 0 = the latest "now"). The ordering invariant is
/// resolved by list position so it matches the page's own newest-first convention regardless of clock skew between
/// two checkpoints stamped in the same instant.
/// </param>
public sealed record TemporalDiffSelection(
    string? FromCheckpointId,
    string? ToCheckpointId,
    IReadOnlyList<TemporalCheckpoint> Checkpoints);

/// <summary>
/// Pure client-side validator for the Operate temporal diff selection: both checkpoints must be chosen and the
/// from (as-of) checkpoint must be chronologically &lt;= the to (now) checkpoint (the catalog's
/// <c>Operate diff FromCheckpoint&lt;=ToCheckpoint</c> ordering rule). The diff action is gated on a clean
/// selection. Mirrors the <see cref="StudioMapValidator"/> pattern; keyed by <see cref="TemporalDiffFieldKeys"/>.
/// </summary>
public sealed class TemporalDiffSelectionValidator : IFieldValidator<TemporalDiffSelection>
{
    /// <summary>Shared singleton; the validator holds no state.</summary>
    public static TemporalDiffSelectionValidator Instance { get; } = new();

    /// <inheritdoc />
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
