using Honua.Console.Contracts;
using System.Text.Json.Serialization;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;
using Microsoft.AspNetCore.Antiforgery;

namespace Honua.Console.Web.Auth;

internal static class ConsoleHumanProposalBff
{
    public static void MapConsoleHumanProposalBff(this WebApplication app)
    {
        app.MapGet("/api/console/proposals/session", ([AsParameters] ConsoleHumanProposalEndpointContext endpoint) =>
            OpenBrowserSessionAsync(endpoint));
        app.MapPost("/api/console/proposals/{id}/review", async (
            [AsParameters] ConsoleHumanProposalEndpointContext endpoint, string id, ConsoleHumanProposalRequest request) =>
        {
            var profile = await AuthorizeAsync(endpoint, request).ConfigureAwait(false);
            if (profile is null) return Results.StatusCode(StatusCodes.Status403Forbidden);
            var result = await endpoint.Sessions.OpenReviewAsync(
                new ConsoleHumanProposalTarget(profile, request.Authority, id), endpoint.Context.RequestAborted).ConfigureAwait(false);
            return Results.Json(result, ConsoleHumanProposalBffJsonContext.Default.OperateSectionResultConsoleHumanProposalReview);
        });
        app.MapPost("/api/console/proposals/{id}/approve", async (
            [AsParameters] ConsoleHumanProposalEndpointContext endpoint, string id, ConsoleHumanProposalRequest request) =>
        {
            var profile = await AuthorizeAsync(endpoint, request).ConfigureAwait(false);
            if (profile is null || request.Decision is not { } decision || decision.ProposalId != id)
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            var result = await endpoint.Sessions.ApproveAsync(
                new ConsoleHumanProposalApprovalRequest(profile, request.Authority, decision), endpoint.Context.RequestAborted).ConfigureAwait(false);
            return Results.Json(result, ConsoleHumanProposalBffJsonContext.Default.OperateSectionResultProposalDetailWire);
        });
        app.MapPost("/api/console/proposals/{id}/status", async (
            [AsParameters] ConsoleHumanProposalEndpointContext endpoint, string id, ConsoleHumanProposalRequest request) =>
        {
            var profile = await AuthorizeAsync(endpoint, request).ConfigureAwait(false);
            if (profile is null) return Results.StatusCode(StatusCodes.Status403Forbidden);
            var result = await endpoint.Sessions.ReadStatusAsync(
                new ConsoleHumanProposalTarget(profile, request.Authority, id), endpoint.Context.RequestAborted).ConfigureAwait(false);
            return Results.Json(result, ConsoleHumanProposalBffJsonContext.Default.OperateSectionResultProposalDetailWire);
        });
    }

    private static async Task<IResult> OpenBrowserSessionAsync(
        ConsoleHumanProposalEndpointContext endpoint)
    {
        var context = endpoint.Context;
        context.Response.Headers.CacheControl = "no-store";
        if (!ConsoleApprovalBrowserChannel.HasUnmixedIdentity(context)) return Results.StatusCode(StatusCodes.Status403Forbidden);
        var profile = await endpoint.Profiles.GetActiveProfileAsync(context.RequestAborted).ConfigureAwait(false);
        if (profile is null) return Results.StatusCode(StatusCodes.Status409Conflict);
        if (!endpoint.BrowserChannel.HasSession(profile, endpoint.ServerSessions))
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        var csrf = endpoint.Antiforgery.GetAndStoreTokens(context);
        return Results.Json(new ConsoleHumanProposalBrowserSession(profile.Id, csrf.RequestToken),
            ConsoleHumanProposalBffJsonContext.Default.ConsoleHumanProposalBrowserSession);
    }

    private static async Task<ConsoleEnvironmentProfile?> AuthorizeAsync(
        ConsoleHumanProposalEndpointContext endpoint, ConsoleHumanProposalRequest request)
    {
        var context = endpoint.Context;
        context.Response.Headers.CacheControl = "no-store";
        if (!ConsoleApprovalBrowserChannel.HasUnmixedIdentity(context) || !Enum.IsDefined(request.Authority)) return null;
        try { await endpoint.Antiforgery.ValidateRequestAsync(context).ConfigureAwait(false); }
        catch (AntiforgeryValidationException) { return null; }
        var profile = await endpoint.Profiles.GetActiveProfileAsync(context.RequestAborted).ConfigureAwait(false);
        return profile?.Id == request.ProfileId ? profile : null;
    }

}

internal sealed record ConsoleHumanProposalEndpointContext(
    HttpContext Context, IAntiforgery Antiforgery,
    IConsoleEnvironmentProfileStore Profiles, ConsoleHumanProposalSession Sessions,
    ConsoleApprovalBrowserChannel BrowserChannel, ConsoleServerSessionClientStore ServerSessions);

internal sealed record ConsoleHumanProposalRequest(
    string ProfileId, ConsoleServerAuthority Authority, ConsoleProposalDecision? Decision);

internal sealed record ConsoleHumanProposalBrowserSession(string ProfileId, string? CsrfToken);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(ConsoleHumanProposalRequest))]
[JsonSerializable(typeof(ConsoleHumanProposalBrowserSession))]
[JsonSerializable(typeof(OperateSectionResult<ConsoleHumanProposalReview>))]
[JsonSerializable(typeof(OperateSectionResult<ProposalDetailWire>))]
internal sealed partial class ConsoleHumanProposalBffJsonContext : JsonSerializerContext;
