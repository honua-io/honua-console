namespace Honua.Console.Shell.Models;

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

public sealed class CollectAutomationDraft
{
    public string DraftId { get; set; } = string.Empty;
    public string ContentItemId { get; set; } = string.Empty;
    public string CurrentVersionId { get; set; } = string.Empty;
    public int VersionNumber { get; set; }
    public string PackageType { get; set; } = CollectAutomationContractValues.PackageType;
    public string SchemaVersion { get; set; } = CollectAutomationContractValues.SchemaVersion;

    public string FormId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;

    public int MaxCascadeDepth { get; set; } = 8;

    public List<CollectAutomationRule> Rules { get; set; } = [];
    public List<CollectAutomationValidationIssue> ValidationIssues { get; set; } = [];

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastSavedAt { get; set; }
}

public sealed class CollectAutomationRule
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Trigger { get; set; } = CollectAutomationContractValues.TriggerFieldChange;

    // Empty means the trigger is form-scoped, not one field.
    public string TriggerField { get; set; } = string.Empty;

    // Empty means the trigger always matches.
    public string Condition { get; set; } = string.Empty;

    public List<CollectAutomationAction> Actions { get; set; } = [];
}

public sealed class CollectAutomationAction
{
    public string Id { get; set; } = string.Empty;
    public string Kind { get; set; } = CollectAutomationContractValues.ActionSet;

    public string Target { get; set; } = string.Empty;

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

public sealed record CollectAutomationBindingState(string Surface, string State, string Contract, string Detail);

public sealed record CollectAutomationEditorContext(
    CollectAutomationBindingState? BindingState,
    CollectAutomationDraft? Draft);

public sealed class CollectAutomationSaveResult
{
    public string ContentItemId { get; set; } = string.Empty;
    public string VersionId { get; set; } = string.Empty;
    public int VersionNumber { get; set; }
    public string Contract { get; set; } = "content-version/v1 + collect.automation/v1";
    public IReadOnlyList<CollectAutomationValidationIssue> ValidationIssues { get; set; } = [];

    public CollectAutomationBindingState? BindingState { get; set; }
}

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

public sealed class CollectAutomationRestoreResult
{
    public string ContentItemId { get; set; } = string.Empty;
    public string RestoredFromVersionId { get; set; } = string.Empty;
    public string NewVersionId { get; set; } = string.Empty;
    public int NewVersionNumber { get; set; }

    public CollectAutomationBindingState? BindingState { get; set; }
}
