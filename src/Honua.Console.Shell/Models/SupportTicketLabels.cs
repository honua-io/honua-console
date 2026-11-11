namespace Honua.Console.Shell.Models;

internal static class SupportTicketLabels
{
    public static string? Display(string? value) => value switch
    {
        "sev1" => "Sev1 - critical / outage",
        "sev2" => "Sev2 - major impact",
        "sev3" => "Sev3 - minor / question",
        "read-only" => "Read-only",
        "guided-fix" => "Guided fix",
        "operator-scoped" => "Operator scoped",
        "disabled" => "Disabled",
        "resolved" => "Resolved",
        "cancelled" => "Cancelled",
        "closed" => "Closed",
        "diagnosed" => "Diagnosed",
        "in progress" => "In progress",
        _ => value
    };
}
