namespace Honua.Console.Shell.Models;

// Agent context shared by the layer tool pages (filter, editing, relationships).
public static class ConsoleAgentLayerTool
{
    public static string LayerRoute(string canonicalResourceId) =>
        $"/operate/layers/{Uri.EscapeDataString(canonicalResourceId)}";

    public static ConsoleAgentPageContext Context(
        OperateServiceLayerProjection? layer, string resourceId, string tool, int changedFields, bool pending)
    {
        IReadOnlyList<ConsoleAgentEntity> selected = layer is null ? [] : [ConsoleAgentEntity.ForLayer(layer)];
        return new()
        {
            Route = $"{LayerRoute(resourceId)}/{tool}",
            Entities = selected,
            Selection = selected,
            Unsaved = new() { ChangedFieldCount = changedFields, PendingOperationCount = pending ? 1 : 0 }
        };
    }
}
