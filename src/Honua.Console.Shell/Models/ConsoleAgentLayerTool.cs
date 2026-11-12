namespace Honua.Console.Shell.Models;

// Agent context shared by the layer tool pages (filter, editing, relationships).
public static class ConsoleAgentLayerTool
{
    public const string Filter = "filter";
    public const string Metadata = "metadata";
    public const string Relationships = "relationships";

    public static string ToolRoute(string resourceId, string tool) => $"{ConsoleAgentRoutes.ForRequestedLayer(resourceId)}/{tool}";

    public static ConsoleAgentPageContext Context(OperateServiceLayerProjection? layer, string route, ConsoleAgentUnsavedSummary unsaved)
    {
        IReadOnlyList<ConsoleAgentEntity> selected = layer is null ? [] : [ConsoleAgentEntity.ForLayer(layer)];
        return new() { Route = route, Entities = selected, Selection = selected, Unsaved = unsaved };
    }
}
