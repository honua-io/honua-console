using System.Globalization;
using Honua.Console.Contracts;

namespace Honua.Console.Shell.Models;

public enum ConsoleActivityActorFilter
{
    Any,
    Person,
    Agent,
    ApiKey,
    System,
}

public sealed record ConsoleActivityFeedQuery(ConsoleActivityActorFilter Actor, string? Cursor = null);

public sealed record ConsoleActivityFeedPage(IReadOnlyList<ObservabilityAuditRecordResponse> Items, string? NextCursor);

public sealed record ConsoleActivityActor(string Name, string Channel, string Kind);

public static class ConsoleActivityPresentation
{
    public const int PageSize = 25;

    // Audit event types that record a change; authentication, authorization and export reads are not changes.
    public const string ChangeEventTypes = "AdminAction,ConfigChange,DataDelete";

    private const string ProposalResource = "operation_proposal";

    public static string QueryString(ConsoleActivityFeedQuery query)
    {
        var parts = new List<string> { $"eventType={ChangeEventTypes}", $"pageSize={PageSize}" };
        var actorType = query.Actor switch
        {
            ConsoleActivityActorFilter.Person => "UserId",
            ConsoleActivityActorFilter.ApiKey => "ApiKey",
            ConsoleActivityActorFilter.System => "System",
            _ => null,
        };
        if (actorType is not null) parts.Add($"actorType={actorType}");
        if (!string.IsNullOrWhiteSpace(query.Cursor)) parts.Add($"cursor={Uri.EscapeDataString(query.Cursor)}");
        return "?" + string.Join('&', parts);
    }

    // Agent attribution takes precedence over the underlying principal type.
    public static IReadOnlyList<ObservabilityAuditRecordResponse> Visible(
        IReadOnlyList<ObservabilityAuditRecordResponse> items, ConsoleActivityActorFilter filter) =>
        filter == ConsoleActivityActorFilter.Any
            ? items
            : items.Where(item => ClassifyActor(item) == filter).ToArray();

    private static ConsoleActivityActorFilter ClassifyActor(ObservabilityAuditRecordResponse row) =>
        Clean(row.ActorAgent) is not null ? ConsoleActivityActorFilter.Agent : row.ActorType switch
        {
            "UserId" => ConsoleActivityActorFilter.Person,
            "ApiKey" => ConsoleActivityActorFilter.ApiKey,
            "System" => ConsoleActivityActorFilter.System,
            _ => ConsoleActivityActorFilter.Any,
        };

    public static ConsoleActivityActor Actor(ObservabilityAuditRecordResponse row)
    {
        var principal = Clean(row.Actor) ?? "Not reported";
        if (Clean(row.ActorAgent) is { } agent)
        {
            return new(agent, row.ActorType == "ApiKey" ? "Agent · API key" : $"Agent · on behalf of {principal}", "agent");
        }
        return ClassifyActor(row) switch
        {
            ConsoleActivityActorFilter.ApiKey => new("API key", principal, "key"),
            ConsoleActivityActorFilter.System => new(principal, "System", "system"),
            _ when row.ActorType == "Anonymous" => new("Anonymous", "Not signed in", "person"),
            _ => new(principal, "Person", "person"),
        };
    }

    public static string Humanize(string? value)
    {
        var words = (Clean(value) ?? "Not reported").Replace('.', ' ').Replace('_', ' ');
        return char.ToUpper(words[0], CultureInfo.InvariantCulture) + words[1..];
    }

    public static string Range(int pagesBefore, int shown) => shown switch
    {
        0 => $"No changes on page {pagesBefore + 1}",
        1 => $"1 change on page {pagesBefore + 1}, newest first",
        _ => $"{shown} changes on page {pagesBefore + 1}, newest first",
    };

    public static string What(ObservabilityAuditRecordResponse row) =>
        Clean(row.ResourceId) is { } id ? $"{Humanize(row.ResourceType)} {id}" : Humanize(row.ResourceType);

    public static bool Succeeded(ObservabilityAuditRecordResponse row) =>
        string.Equals(row.Outcome, "Success", StringComparison.OrdinalIgnoreCase);

    public static string? ReferenceHref(ObservabilityAuditRecordResponse row)
    {
        if (row.ResourceType == ProposalResource && Clean(row.ResourceId) is { } proposal)
        {
            return CorrelationIdRoutes.Resolve(CorrelationIdKind.ProposalId, proposal);
        }
        return Clean(row.CorrelationId) is { } correlation
            ? CorrelationIdRoutes.Resolve(CorrelationIdKind.CorrelationId, correlation)
            : null;
    }

    public static string ReferenceLabel(ObservabilityAuditRecordResponse row) =>
        row.ResourceType == ProposalResource ? "proposal" : "related events";

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
