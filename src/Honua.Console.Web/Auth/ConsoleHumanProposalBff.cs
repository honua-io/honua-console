using System.Text.Json.Serialization;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;
using Microsoft.AspNetCore.Antiforgery;

namespace Honua.Console.Web.Auth;

internal static class ConsoleHumanProposalBff
{
    public static void MapConsoleHumanProposalBff(this WebApplication app)
    {
        app.MapGet("/api/console/proposals/session", (HttpContext context,
            IAntiforgery antiforgery, IConsoleEnvironmentProfileStore profiles, CancellationToken token) =>
            OpenBrowserSessionAsync(context, antiforgery, profiles, token));
        app.MapPost("/api/console/proposals/{id}/review", async (HttpContext context, string id,
            ConsoleHumanProposalRequest request, IAntiforgery antiforgery,
            IConsoleEnvironmentProfileStore profiles, ConsoleHumanProposalSession sessions, CancellationToken token) =>
        {
            var profile = await AuthorizeAsync(context, request, antiforgery, profiles, token).ConfigureAwait(false);
            if (profile is null) return Results.StatusCode(StatusCodes.Status403Forbidden);
            var result = await sessions.OpenReviewAsync(profile, request.Authority, id, token).ConfigureAwait(false);
            return Results.Json(result, ConsoleHumanProposalBffJsonContext.Default.OperateSectionResultConsoleHumanProposalReview);
        });
        app.MapPost("/api/console/proposals/{id}/approve", async (HttpContext context, string id,
            ConsoleHumanProposalRequest request, IAntiforgery antiforgery,
            IConsoleEnvironmentProfileStore profiles, ConsoleHumanProposalSession sessions, CancellationToken token) =>
        {
            var profile = await AuthorizeAsync(context, request, antiforgery, profiles, token).ConfigureAwait(false);
            if (profile is null || request.Decision is not { } decision || decision.ProposalId != id)
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            var result = await sessions.ApproveAsync(profile, request.Authority, decision, token).ConfigureAwait(false);
            return Results.Json(result, ConsoleHumanProposalBffJsonContext.Default.OperateSectionResultProposalDetailWire);
        });
    }

    private static async Task<IResult> OpenBrowserSessionAsync(
        HttpContext context, IAntiforgery antiforgery, IConsoleEnvironmentProfileStore profiles, CancellationToken token)
    {
        context.Response.Headers.CacheControl = "no-store";
        if (!IsBrowserSession(context)) return Results.StatusCode(StatusCodes.Status403Forbidden);
        var profile = await profiles.GetActiveProfileAsync(token).ConfigureAwait(false);
        if (profile is null) return Results.StatusCode(StatusCodes.Status409Conflict);
        var csrf = antiforgery.GetAndStoreTokens(context);
        return Results.Json(new ConsoleHumanProposalBrowserSession(profile.Id, csrf.RequestToken),
            ConsoleHumanProposalBffJsonContext.Default.ConsoleHumanProposalBrowserSession);
    }

    private static async Task<ConsoleEnvironmentProfile?> AuthorizeAsync(
        HttpContext context, ConsoleHumanProposalRequest request, IAntiforgery antiforgery,
        IConsoleEnvironmentProfileStore profiles, CancellationToken token)
    {
        context.Response.Headers.CacheControl = "no-store";
        if (!IsBrowserSession(context) || !Enum.IsDefined(request.Authority)) return null;
        try { await antiforgery.ValidateRequestAsync(context).ConfigureAwait(false); }
        catch (AntiforgeryValidationException) { return null; }
        var profile = await profiles.GetActiveProfileAsync(token).ConfigureAwait(false);
        return profile?.Id == request.ProfileId ? profile : null;
    }

    private static bool IsBrowserSession(HttpContext context) => context.User.Identity?.IsAuthenticated == true
        && !context.Request.Headers.ContainsKey("Authorization") && !context.Request.Headers.ContainsKey("X-API-Key");
}

internal sealed record ConsoleHumanProposalRequest(
    string ProfileId, ConsoleProposalAuthority Authority, ConsoleProposalDecision? Decision);

internal sealed record ConsoleHumanProposalBrowserSession(string ProfileId, string? CsrfToken);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(ConsoleHumanProposalRequest))]
[JsonSerializable(typeof(ConsoleHumanProposalBrowserSession))]
[JsonSerializable(typeof(OperateSectionResult<ConsoleHumanProposalReview>))]
[JsonSerializable(typeof(OperateSectionResult<ProposalDetailWire>))]
internal sealed partial class ConsoleHumanProposalBffJsonContext : JsonSerializerContext;
