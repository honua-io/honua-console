using System.Text.Json;

namespace Honua.Console.Shell.Models;

/// <summary>
/// Stable content fingerprint for a <see cref="CollectAutomationDraft"/>. The editor compares the current
/// draft against its last-saved fingerprint to know whether a new immutable version is needed before a
/// restore/save, mirroring <see cref="StudioWorkflowPackageDraftContent"/>. Only the body that the engine
/// runs is hashed (not server-assigned ids/timestamps).
/// </summary>
internal static class CollectAutomationDraftContent
{
    private static readonly JsonSerializerOptions FingerprintOptions = new(JsonSerializerDefaults.Web);

    public static string CreateFingerprint(CollectAutomationDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);

        return JsonSerializer.Serialize(CreateSnapshot(draft), FingerprintOptions);
    }

    private static object CreateSnapshot(CollectAutomationDraft draft) =>
        new
        {
            draft.PackageType,
            draft.SchemaVersion,
            draft.FormId,
            draft.Name,
            draft.Description,
            draft.Enabled,
            draft.MaxCascadeDepth,
            Rules = draft.Rules.Select(rule => new
            {
                rule.Id,
                rule.Name,
                rule.Trigger,
                rule.TriggerField,
                rule.Condition,
                Actions = rule.Actions.Select(action => new
                {
                    action.Id,
                    action.Kind,
                    action.Target,
                    action.Expression
                }).ToArray()
            }).ToArray()
        };
}
