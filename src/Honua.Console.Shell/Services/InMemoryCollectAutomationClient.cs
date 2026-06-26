using System.Text.Json;
using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Transient in-memory simulator for <see cref="ICollectAutomationClient"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Do not promote to a merged data source.</b> This client fakes the server/Collect-owned automation
/// content + version lifecycle so Console UX and unit tests can be built ahead of the projection contract.
/// Per <c>docs/migration/CONSOLE_PATTERNS_CHARTER.md</c> §11 it is a scaffold only; the merged runtime stays
/// on <see cref="UnsupportedCollectAutomationClient"/> until the real projection lands. It deliberately
/// enforces the version-lifecycle invariants (monotonic versions, validation-gated commits, append-only
/// restore) the live replacement must preserve.
/// </para>
/// </remarks>
public sealed class InMemoryCollectAutomationClient : ICollectAutomationClient
{
    public const string SeedDraftId = "automation-draft-permit-intake";

    private static readonly JsonSerializerOptions CloneOptions = new(JsonSerializerDefaults.Web);

    private readonly object _gate = new();
    private readonly Dictionary<string, CollectAutomationDraft> _drafts = new(StringComparer.Ordinal);

    // Append-only version history per content item, oldest first.
    private readonly Dictionary<string, List<StoredVersion>> _versions = new(StringComparer.Ordinal);
    private int _draftSequence = 1;

    public InMemoryCollectAutomationClient(IEnumerable<CollectAutomationDraft>? seedDrafts = null)
    {
        foreach (var draft in seedDrafts ?? [CreateSeedDraft()])
        {
            _drafts[draft.DraftId] = Clone(draft);
            SeedInitialVersion(draft);
        }
    }

    public static InMemoryCollectAutomationClient CreateSeeded() => new();

    public Task<IReadOnlyList<CollectAutomationSummary>> ListAutomationsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<CollectAutomationSummary>>(
                _drafts.Values
                    .OrderByDescending(draft => draft.UpdatedAt)
                    .Select(draft => new CollectAutomationSummary
                    {
                        DraftId = draft.DraftId,
                        ContentItemId = draft.ContentItemId,
                        Name = draft.Name,
                        FormId = draft.FormId,
                        Enabled = draft.Enabled,
                        RuleCount = draft.Rules.Count,
                        VersionNumber = draft.VersionNumber,
                        UpdatedAt = draft.UpdatedAt
                    })
                    .ToArray());
        }
    }

    public Task<CollectAutomationEditorContext> OpenEditorAsync(string? draftId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            if (string.IsNullOrWhiteSpace(draftId) || string.Equals(draftId, "new", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(new CollectAutomationEditorContext(BindingState: null, CreateDraftLocked()));
            }

            var draft = _drafts.TryGetValue(draftId, out var stored) ? Clone(stored) : null;
            return Task.FromResult(new CollectAutomationEditorContext(BindingState: null, draft));
        }
    }

    public Task<CollectAutomationSaveResult> SaveVersionAsync(
        CollectAutomationDraft draft,
        string changeNote,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentException.ThrowIfNullOrWhiteSpace(draft.DraftId);
        ArgumentException.ThrowIfNullOrWhiteSpace(changeNote);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            var issues = Validate(draft);

            // A validation failure creates no immutable version: return the issues with no version id so the
            // editor keeps the draft dirty rather than silently committing a broken automation.
            if (issues.Any(issue => string.Equals(issue.Severity, "error", StringComparison.OrdinalIgnoreCase)))
            {
                draft.ValidationIssues = Clone(issues);
                return Task.FromResult(new CollectAutomationSaveResult
                {
                    ContentItemId = draft.ContentItemId,
                    ValidationIssues = issues
                });
            }

            var stored = Clone(draft);
            stored.ContentItemId = string.IsNullOrWhiteSpace(stored.ContentItemId)
                ? $"automation-{Slugify(stored.Name)}"
                : stored.ContentItemId;

            var previousVersionNumber = _drafts.TryGetValue(stored.DraftId, out var existing) ? existing.VersionNumber : 0;
            stored.VersionNumber = Math.Max(stored.VersionNumber, previousVersionNumber) + 1;
            stored.CurrentVersionId = $"{stored.ContentItemId}:v{stored.VersionNumber}";
            stored.LastSavedAt = DateTimeOffset.UtcNow;
            stored.UpdatedAt = stored.LastSavedAt.Value;
            stored.ValidationIssues = Clone(issues);

            _drafts[stored.DraftId] = Clone(stored);
            AppendVersion(stored, changeNote);

            // Mirror server-assigned identity back onto the caller's draft.
            draft.ContentItemId = stored.ContentItemId;
            draft.VersionNumber = stored.VersionNumber;
            draft.CurrentVersionId = stored.CurrentVersionId;
            draft.LastSavedAt = stored.LastSavedAt;
            draft.UpdatedAt = stored.UpdatedAt;
            draft.ValidationIssues = Clone(issues);

            return Task.FromResult(new CollectAutomationSaveResult
            {
                ContentItemId = stored.ContentItemId,
                VersionId = stored.CurrentVersionId,
                VersionNumber = stored.VersionNumber,
                ValidationIssues = issues
            });
        }
    }

    public Task<CollectAutomationVersionHistory> ListVersionsAsync(
        string contentItemId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(contentItemId))
        {
            return Task.FromResult(CollectAutomationVersionHistory.Empty);
        }

        lock (_gate)
        {
            if (!_versions.TryGetValue(contentItemId, out var stored) || stored.Count == 0)
            {
                return Task.FromResult(CollectAutomationVersionHistory.Empty);
            }

            var currentVersionId = _drafts.Values
                .FirstOrDefault(draft => string.Equals(draft.ContentItemId, contentItemId, StringComparison.Ordinal))
                ?.CurrentVersionId;

            var versions = stored
                .OrderByDescending(version => version.VersionNumber)
                .Select(version => new CollectAutomationVersion
                {
                    VersionId = version.VersionId,
                    VersionNumber = version.VersionNumber,
                    ContentItemId = contentItemId,
                    ChangeNote = version.ChangeNote,
                    Author = version.Author,
                    RuleCount = version.Body.Rules.Count,
                    ActionCount = version.Body.Rules.Sum(rule => rule.Actions.Count),
                    IsCurrent = string.Equals(version.VersionId, currentVersionId, StringComparison.Ordinal),
                    CommittedAt = version.CommittedAt
                })
                .ToArray();

            return Task.FromResult(new CollectAutomationVersionHistory(versions));
        }
    }

    public Task<CollectAutomationDraft?> GetVersionAsync(
        string contentItemId,
        string versionId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            if (!_versions.TryGetValue(contentItemId, out var stored))
            {
                return Task.FromResult<CollectAutomationDraft?>(null);
            }

            var version = stored.FirstOrDefault(v => string.Equals(v.VersionId, versionId, StringComparison.Ordinal));
            return Task.FromResult(version is null ? null : Clone(version.Body));
        }
    }

    public Task<CollectAutomationRestoreResult> RestoreVersionAsync(
        string contentItemId,
        string versionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentItemId);
        ArgumentException.ThrowIfNullOrWhiteSpace(versionId);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            if (!_versions.TryGetValue(contentItemId, out var stored))
            {
                throw new InvalidOperationException($"No version history for content item '{contentItemId}'.");
            }

            var source = stored.FirstOrDefault(v => string.Equals(v.VersionId, versionId, StringComparison.Ordinal))
                ?? throw new InvalidOperationException($"Version '{versionId}' not found.");

            var draft = _drafts.Values.FirstOrDefault(d =>
                string.Equals(d.ContentItemId, contentItemId, StringComparison.Ordinal))
                ?? throw new InvalidOperationException($"No draft bound to content item '{contentItemId}'.");

            // Append-only restore: re-instate the prior body as the live draft body, then commit it as a NEW
            // version. The original version is never mutated.
            var restoredBody = Clone(source.Body);
            restoredBody.DraftId = draft.DraftId;
            restoredBody.ContentItemId = contentItemId;
            restoredBody.VersionNumber = draft.VersionNumber;
            restoredBody.CurrentVersionId = draft.CurrentVersionId;

            restoredBody.VersionNumber += 1;
            restoredBody.CurrentVersionId = $"{contentItemId}:v{restoredBody.VersionNumber}";
            restoredBody.LastSavedAt = DateTimeOffset.UtcNow;
            restoredBody.UpdatedAt = restoredBody.LastSavedAt.Value;
            restoredBody.ValidationIssues = Validate(restoredBody);

            _drafts[draft.DraftId] = Clone(restoredBody);
            AppendVersion(restoredBody, $"Restored from {versionId}.");

            return Task.FromResult(new CollectAutomationRestoreResult
            {
                ContentItemId = contentItemId,
                RestoredFromVersionId = versionId,
                NewVersionId = restoredBody.CurrentVersionId,
                NewVersionNumber = restoredBody.VersionNumber
            });
        }
    }

    private CollectAutomationDraft CreateDraftLocked()
    {
        _draftSequence += 1;
        var draft = new CollectAutomationDraft
        {
            DraftId = $"automation-draft-new-{_draftSequence:000}",
            Name = "Untitled automation",
            Description = "Trigger-bound rules driving the Collect Data Events engine.",
            FormId = string.Empty,
            Enabled = true,
            MaxCascadeDepth = 8,
            Rules =
            [
                new CollectAutomationRule
                {
                    Id = "rule-1",
                    Name = "New rule",
                    Trigger = CollectAutomationContractValues.TriggerFieldChange,
                    Actions =
                    [
                        new CollectAutomationAction
                        {
                            Id = "action-1",
                            Kind = CollectAutomationContractValues.ActionSet
                        }
                    ]
                }
            ],
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        _drafts[draft.DraftId] = Clone(draft);
        return Clone(draft);
    }

    private void SeedInitialVersion(CollectAutomationDraft draft)
    {
        if (string.IsNullOrWhiteSpace(draft.ContentItemId) || draft.VersionNumber <= 0)
        {
            return;
        }

        AppendVersion(draft, "Initial version.");
    }

    private void AppendVersion(CollectAutomationDraft draft, string changeNote)
    {
        if (!_versions.TryGetValue(draft.ContentItemId, out var history))
        {
            history = [];
            _versions[draft.ContentItemId] = history;
        }

        history.Add(new StoredVersion
        {
            VersionId = draft.CurrentVersionId,
            VersionNumber = draft.VersionNumber,
            ChangeNote = changeNote,
            Author = "mike@honua.io",
            Body = Clone(draft),
            CommittedAt = DateTimeOffset.UtcNow
        });
    }

    private static List<CollectAutomationValidationIssue> Validate(CollectAutomationDraft draft)
    {
        var issues = new List<CollectAutomationValidationIssue>();

        if (string.IsNullOrWhiteSpace(draft.Name))
        {
            issues.Add(new() { Severity = "error", Scope = "automation", Message = "Automation must declare a name." });
        }

        if (string.IsNullOrWhiteSpace(draft.FormId))
        {
            issues.Add(new() { Severity = "error", Scope = "binding", Message = "Automation must bind to a form." });
        }

        if (draft.Rules.Count == 0)
        {
            issues.Add(new() { Severity = "error", Scope = "rules", Message = "Automation must include at least one rule." });
        }

        foreach (var rule in draft.Rules)
        {
            if (!CollectAutomationContractValues.TriggerKinds.Contains(rule.Trigger))
            {
                issues.Add(new() { Severity = "error", Scope = "rules", Message = $"Rule '{rule.Name}' has an unsupported trigger." });
            }

            if (rule.Actions.Count == 0)
            {
                issues.Add(new() { Severity = "error", Scope = "rules", Message = $"Rule '{rule.Name}' has no actions." });
            }

            foreach (var action in rule.Actions)
            {
                if (!CollectAutomationContractValues.ActionKinds.Contains(action.Kind))
                {
                    issues.Add(new() { Severity = "error", Scope = "actions", Message = $"Action in rule '{rule.Name}' has an unsupported kind." });
                }
            }
        }

        return issues;
    }

    private static CollectAutomationDraft CreateSeedDraft()
    {
        var now = DateTimeOffset.UtcNow;

        return new CollectAutomationDraft
        {
            DraftId = SeedDraftId,
            ContentItemId = "automation-permit-intake",
            CurrentVersionId = "automation-permit-intake:v1",
            VersionNumber = 1,
            FormId = "form-permit-intake",
            Name = "Permit intake automation",
            Description = "Compute fees, validate parcels, tag escalations, and notify reviewers on submit.",
            Enabled = true,
            MaxCascadeDepth = 8,
            CreatedAt = now,
            UpdatedAt = now,
            LastSavedAt = now,
            Rules =
            [
                new CollectAutomationRule
                {
                    Id = "rule-compute-fee",
                    Name = "Compute permit fee",
                    Trigger = CollectAutomationContractValues.TriggerFieldChange,
                    TriggerField = "permit_type",
                    Condition = "permit_type != null",
                    Actions =
                    [
                        new CollectAutomationAction
                        {
                            Id = "action-fee",
                            Kind = CollectAutomationContractValues.ActionCompute,
                            Target = "fee_amount",
                            Expression = "feeSchedule(permit_type, square_feet)"
                        }
                    ]
                },
                new CollectAutomationRule
                {
                    Id = "rule-validate-parcel",
                    Name = "Validate parcel",
                    Trigger = CollectAutomationContractValues.TriggerBeforeSubmit,
                    Condition = "parcel_id == null || !isParcel(parcel_id)",
                    Actions =
                    [
                        new CollectAutomationAction
                        {
                            Id = "action-validate",
                            Kind = CollectAutomationContractValues.ActionValidate,
                            Expression = "Parcel id is required and must be a known parcel."
                        }
                    ]
                },
                new CollectAutomationRule
                {
                    Id = "rule-escalate",
                    Name = "Escalate high-value permits",
                    Trigger = CollectAutomationContractValues.TriggerAfterSubmit,
                    Condition = "fee_amount > 10000",
                    Actions =
                    [
                        new CollectAutomationAction
                        {
                            Id = "action-tag",
                            Kind = CollectAutomationContractValues.ActionTag,
                            Target = "escalation",
                            Expression = "high-value"
                        },
                        new CollectAutomationAction
                        {
                            Id = "action-notify",
                            Kind = CollectAutomationContractValues.ActionNotify,
                            Target = "reviewers",
                            Expression = "High-value permit submitted: {{permit_id}}."
                        }
                    ]
                }
            ]
        };
    }

    private static string Slugify(string value)
    {
        var chars = value
            .Trim()
            .ToLowerInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '-')
            .ToArray();

        var compact = string.Join(
            '-',
            new string(chars).Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        return string.IsNullOrWhiteSpace(compact) ? "automation" : compact;
    }

    private static T Clone<T>(T value)
    {
        var json = JsonSerializer.Serialize(value, CloneOptions);
        return JsonSerializer.Deserialize<T>(json, CloneOptions)
            ?? throw new InvalidOperationException($"Could not clone {typeof(T).Name}.");
    }

    private sealed class StoredVersion
    {
        public string VersionId { get; init; } = string.Empty;
        public int VersionNumber { get; init; }
        public string ChangeNote { get; init; } = string.Empty;
        public string Author { get; init; } = string.Empty;
        public CollectAutomationDraft Body { get; init; } = new();
        public DateTimeOffset CommittedAt { get; init; } = DateTimeOffset.UtcNow;
    }
}
