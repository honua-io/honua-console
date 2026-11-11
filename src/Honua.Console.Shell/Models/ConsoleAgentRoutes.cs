namespace Honua.Console.Shell.Models;

public static class ConsoleAgentRoutes
{
    public static string ForEntity(ConsoleAgentEntity target)
    {
        ArgumentNullException.ThrowIfNull(target);
        var id = Segment(target.Id);
        return target.Kind switch
        {
            ConsoleAgentEntityKind.Folder => $"/operate/folders/{id}",
            ConsoleAgentEntityKind.Service => $"/operate/services/{Segment(target.ServiceName ?? target.Id)}/settings",
            ConsoleAgentEntityKind.Layer => $"/operate/layers/{Segment(target.ResourceId ?? target.Id)}",
            ConsoleAgentEntityKind.Job => $"/operate/jobs/{id}",
            ConsoleAgentEntityKind.Approval => $"/inbox?proposalId={id}",
            ConsoleAgentEntityKind.Item => $"/share/manage?itemId={id}",
            _ => throw new ArgumentOutOfRangeException(nameof(target))
        };
    }

    private static string Segment(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.Length > 256 || value.Any(char.IsControl) || value is "." or "..")
        {
            throw new ArgumentException("Invalid Console entity identifier.", nameof(value));
        }
        return Uri.EscapeDataString(value);
    }
}
