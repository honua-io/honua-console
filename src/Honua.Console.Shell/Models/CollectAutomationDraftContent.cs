using System.Text.Json;

namespace Honua.Console.Shell.Models;

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
