namespace Honua.Console.Shell.Models;

/// <summary>
/// Stable contract vocabulary for the Collect automation authoring surface (honua-console#219). These
/// mirror the shipped Collect Data Events engine (<c>Honua.Collect.Core</c>, honua-collect PRs #58/#84/#94):
/// triggers, sandboxed expression conditions, the set/compute/validate/tag/notify/http/open-url action
/// kinds, and the deterministic cascade + loop guard. The Console authoring UI composes/edits/versions
/// these automations over the engine's contract; it never re-implements the engine.
/// </summary>
public static class CollectAutomationContractValues
{
    public const string PackageType = "collect.automation";
    public const string SchemaVersion = "collect.automation/v1";
    public const string ContentItemType = "automation";

    // Trigger kinds the shipped Data Events engine fires on (form-definition events).
    public const string TriggerFormOpen = "form-open";
    public const string TriggerFieldChange = "field-change";
    public const string TriggerBeforeSubmit = "before-submit";
    public const string TriggerAfterSubmit = "after-submit";
    public const string TriggerRecordCreate = "record-create";

    public static readonly IReadOnlyList<string> TriggerKinds =
    [
        TriggerFormOpen,
        TriggerFieldChange,
        TriggerBeforeSubmit,
        TriggerAfterSubmit,
        TriggerRecordCreate,
    ];

    // Action kinds shipped in the engine's action seam.
    public const string ActionSet = "set";
    public const string ActionCompute = "compute";
    public const string ActionValidate = "validate";
    public const string ActionTag = "tag";
    public const string ActionNotify = "notify";
    public const string ActionHttp = "http";
    public const string ActionOpenUrl = "open-url";
    public const string ActionAi = "ai";

    public static readonly IReadOnlyList<string> ActionKinds =
    [
        ActionSet,
        ActionCompute,
        ActionValidate,
        ActionTag,
        ActionNotify,
        ActionHttp,
        ActionOpenUrl,
        ActionAi,
    ];
}

/// <summary>
/// A console-owned editable draft of a Collect automation: a set of trigger-bound rules whose
/// sandboxed-expression conditions gate an ordered list of engine actions. This is the body the
/// authoring UI composes and the versioning surface snapshots.
/// </summary>
public sealed class CollectAutomationDraft
{
    public string DraftId { get; set; } = string.Empty;
    public string ContentItemId { get; set; } = string.Empty;
    public string CurrentVersionId { get; set; } = string.Empty;
    public int VersionNumber { get; set; }
    public string PackageType { get; set; } = CollectAutomationContractValues.PackageType;
    public string SchemaVersion { get; set; } = CollectAutomationContractValues.SchemaVersion;

    /// <summary>The form definition this automation is authored alongside (the engine binds events from it).</summary>
    public string FormId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;

    /// <summary>Loop-guard cap: the maximum cascade depth the engine evaluates before halting (deterministic).</summary>
    public int MaxCascadeDepth { get; set; } = 8;

    public List<CollectAutomationRule> Rules { get; set; } = [];
    public List<CollectAutomationValidationIssue> ValidationIssues { get; set; } = [];

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastSavedAt { get; set; }
}

/// <summary>A single trigger-bound rule: a condition expression gating an ordered action list.</summary>
public sealed class CollectAutomationRule
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Trigger { get; set; } = CollectAutomationContractValues.TriggerFieldChange;

    /// <summary>The form field that scopes a field-change trigger; empty for form-scoped triggers.</summary>
    public string TriggerField { get; set; } = string.Empty;

    /// <summary>Sandboxed boolean expression evaluated by the engine; empty means "always".</summary>
    public string Condition { get; set; } = string.Empty;

    public List<CollectAutomationAction> Actions { get; set; } = [];
}

/// <summary>A single engine action within a rule. <see cref="Target"/>/<see cref="Expression"/> meaning depends on <see cref="Kind"/>.</summary>
public sealed class CollectAutomationAction
{
    public string Id { get; set; } = string.Empty;
    public string Kind { get; set; } = CollectAutomationContractValues.ActionSet;

    /// <summary>The field/tag/channel the action writes to (set/compute target, tag name, notify channel).</summary>
    public string Target { get; set; } = string.Empty;

    /// <summary>The sandboxed value/condition/message expression the engine evaluates for this action.</summary>
    public string Expression { get; set; } = string.Empty;
}

public sealed class CollectAutomationValidationIssue
{
    public string Severity { get; set; } = "warning";
    public string Scope { get; set; } = "automation";
    public string Message { get; set; } = string.Empty;
}

public sealed class CollectAutomationSummary
{
    public string DraftId { get; set; } = string.Empty;
    public string ContentItemId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string FormId { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public int RuleCount { get; set; }
    public int VersionNumber { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Shared missing-binding / unsupported / forbidden surface for the automation editor. Mirrors
/// <see cref="StudioWorkflowBindingState"/>: when automation data cannot bind to a real honua-server /
/// Collect projection, the editor renders this blocked surface (charter §5/§11) instead of seeded data.
/// </summary>
public sealed record CollectAutomationBindingState(string Surface, string State, string Contract, string Detail);

/// <summary>
/// Result of opening the automation editor for a draft. Carries the draft when bound, or a
/// <see cref="CollectAutomationBindingState"/> when the surface is blocked - in a single call so the page
/// does not chain a separate binding probe.
/// </summary>
public sealed record CollectAutomationEditorContext(
    CollectAutomationBindingState? BindingState,
    CollectAutomationDraft? Draft);

/// <summary>Result of saving an immutable automation version. A version exists only when <see cref="VersionId"/> is set.</summary>
public sealed class CollectAutomationSaveResult
{
    public string ContentItemId { get; set; } = string.Empty;
    public string VersionId { get; set; } = string.Empty;
    public int VersionNumber { get; set; }
    public string Contract { get; set; } = "content-version/v1 + collect.automation/v1";
    public IReadOnlyList<CollectAutomationValidationIssue> ValidationIssues { get; set; } = [];

    /// <summary>Set when the server-backed save could not bind; drives the shared blocked surface.</summary>
    public CollectAutomationBindingState? BindingState { get; set; }
}

/// <summary>A single committed, immutable version in an automation's history (newest first when listed).</summary>
public sealed class CollectAutomationVersion
{
    public string VersionId { get; set; } = string.Empty;
    public int VersionNumber { get; set; }
    public string ContentItemId { get; set; } = string.Empty;
    public string ChangeNote { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public int RuleCount { get; set; }
    public int ActionCount { get; set; }
    public bool IsCurrent { get; set; }
    public DateTimeOffset CommittedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>The version history for a saved automation content item, or a binding state when unbound.</summary>
public sealed class CollectAutomationVersionHistory
{
    public CollectAutomationVersionHistory(IReadOnlyList<CollectAutomationVersion> versions)
    {
        Versions = versions;
    }

    private CollectAutomationVersionHistory(CollectAutomationBindingState bindingState)
    {
        BindingState = bindingState;
        Versions = [];
    }

    public IReadOnlyList<CollectAutomationVersion> Versions { get; }

    public CollectAutomationBindingState? BindingState { get; }

    public static CollectAutomationVersionHistory Empty { get; } = new(Array.Empty<CollectAutomationVersion>());

    public static CollectAutomationVersionHistory Blocked(CollectAutomationBindingState bindingState) => new(bindingState);
}

/// <summary>Result of restoring a prior version: the engine creates a NEW version that re-instates the prior body.</summary>
public sealed class CollectAutomationRestoreResult
{
    public string ContentItemId { get; set; } = string.Empty;
    public string RestoredFromVersionId { get; set; } = string.Empty;
    public string NewVersionId { get; set; } = string.Empty;
    public int NewVersionNumber { get; set; }

    /// <summary>Set when the server-backed restore could not bind; drives the shared blocked surface.</summary>
    public CollectAutomationBindingState? BindingState { get; set; }
}
