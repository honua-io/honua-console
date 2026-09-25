namespace Honua.Console.Shell.Models;

/// <summary>Maps editor sharing vocabulary to the canonical Studio publication contract.</summary>
public static class StudioPublicationVisibility
{
    public static string ToCanonical(string visibility) => visibility switch
    {
        "private" => "personal",
        "workspace" => "team",
        _ => visibility
    };

    public static string ToEditor(string visibility) => visibility switch
    {
        "personal" => "private",
        "team" => "workspace",
        _ => visibility
    };
}
