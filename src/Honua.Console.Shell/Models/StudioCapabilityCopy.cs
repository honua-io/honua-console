namespace Honua.Console.Shell.Models;

/// <summary>
/// Plain-language first lines for Studio capability/error states (honua-console#311). Given a
/// capability-state label (the raw contract vocabulary honua-server returns — "Unsupported",
/// "Missing binding", "Missing permission", ...), returns what the state means for the operator and
/// what they can do. The verbatim technical diagnostics stay in the diagnostics disclosure beneath.
/// </summary>
public static class StudioCapabilityCopy
{
    public static string Summary(string surface, string? state)
    {
        var subject = string.IsNullOrWhiteSpace(surface) ? "This surface" : surface;

        if (string.IsNullOrWhiteSpace(state))
        {
            return $"{subject} couldn't be completed on the connected server.";
        }

        if (state.Contains("permission", StringComparison.OrdinalIgnoreCase))
        {
            return $"Your account can't access {subject.ToLowerInvariant()} on the connected server.";
        }

        if (state.Contains("binding", StringComparison.OrdinalIgnoreCase))
        {
            return $"Connect this Console to a Honua Server to use {subject.ToLowerInvariant()}.";
        }

        if (state.Contains("unsupported", StringComparison.OrdinalIgnoreCase))
        {
            return $"{subject} isn't available on the connected server version yet.";
        }

        if (state.Contains("conflict", StringComparison.OrdinalIgnoreCase))
        {
            return "Someone else changed this on the server — reload and reapply your edit.";
        }

        return $"{subject} couldn't be completed on the connected server.";
    }
}
