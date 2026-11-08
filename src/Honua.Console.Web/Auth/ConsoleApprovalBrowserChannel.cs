using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;
using Microsoft.AspNetCore.DataProtection;

namespace Honua.Console.Web.Auth;

internal sealed class ConsoleApprovalBrowserChannel(
    IDataProtectionProvider protection, IHttpContextAccessor requests, TimeProvider clock)
{
    private const string CookiePrefix = "__Host-HonuaApproval.";
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);
    private readonly IDataProtector _protector = protection.CreateProtector("Honua.Console.ApprovalBrowser.v1");

    public void CompleteCallback(ConsoleApprovalBrowserTarget target)
    {
        var sessionReference = Guid.NewGuid().ToString("N");
        target.Partition.ApprovalSessionReference = sessionReference;
        target.Partition.ProposalReviews.Clear();
        var context = requests.HttpContext;
        if (context is null || !context.Request.IsHttps || !HasUnmixedIdentity(context)
            || ConsoleOperatorContext.ResolveKey(context.User) != target.Partition.Key.OperatorKey) return;
        var grant = new ConsoleApprovalBrowserGrant
        {
            OperatorKey = target.Partition.Key.OperatorKey,
            ProfileBinding = ConsoleServerProfileBinding.Capture(target.SelectedProfile),
            AuthorityBinding = ConsoleServerProfileBinding.Capture(target.AuthorityProfile),
            ServerBaseUri = target.AuthorityProfile.ServerBaseUri.AbsoluteUri,
            Authority = target.Authority,
            SessionReference = sessionReference,
            Audience = Audience(context),
            ExpiresAt = clock.GetUtcNow() + Lifetime
        };
        var value = _protector.Protect(JsonSerializer.Serialize(grant,
            ConsoleApprovalBrowserJsonContext.Default.ConsoleApprovalBrowserGrant));
        context.Response.Cookies.Append(CookieName(grant.ProfileBinding, grant.Authority), value, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = "/",
            Expires = grant.ExpiresAt,
            IsEssential = true
        });
    }

    public bool Authorizes(ConsoleApprovalBrowserTarget target)
    {
        var grant = ReadGrant(ConsoleServerProfileBinding.Capture(target.SelectedProfile), target.Authority);
        return grant is not null && MatchesPartition(grant, target.Partition)
            && grant.AuthorityBinding == ConsoleServerProfileBinding.Capture(target.AuthorityProfile);
    }

    public bool HasSession(ConsoleEnvironmentProfile profile, ConsoleServerSessionClientStore sessions)
    {
        foreach (var authority in Enum.GetValues<ConsoleServerAuthority>())
        {
            var grant = ReadGrant(ConsoleServerProfileBinding.Capture(profile), authority);
            if (grant is null || !Uri.TryCreate(grant.ServerBaseUri, UriKind.Absolute, out var serverBaseUri)) continue;
            var bound = profile with { ServerBaseUri = serverBaseUri };
            if (sessions.TryGet(grant.OperatorKey, bound, out var partition)
                && partition is not null && MatchesPartition(grant, partition)) return true;
        }
        return false;
    }

    private ConsoleApprovalBrowserGrant? ReadGrant(string profileBinding, ConsoleServerAuthority authority)
    {
        var context = requests.HttpContext;
        if (context is null || !HasUnmixedIdentity(context)
            || !context.Request.Cookies.TryGetValue(CookieName(profileBinding, authority), out var value)
            || value.Length > 8192) return null;
        try
        {
            var grant = JsonSerializer.Deserialize(_protector.Unprotect(value),
                ConsoleApprovalBrowserJsonContext.Default.ConsoleApprovalBrowserGrant);
            return grant is not null && grant.ProfileBinding == profileBinding && grant.Authority == authority
                && grant.OperatorKey == ConsoleOperatorContext.ResolveKey(context.User)
                && grant.Audience == Audience(context) && grant.ExpiresAt > clock.GetUtcNow()
                && grant.ExpiresAt <= clock.GetUtcNow() + Lifetime ? grant : null;
        }
        catch (Exception error) when (error is CryptographicException or JsonException or FormatException)
        {
            return null;
        }
    }

    private static bool MatchesPartition(ConsoleApprovalBrowserGrant grant, ConsoleServerSessionPartition partition) =>
        !string.IsNullOrEmpty(grant.SessionReference) && grant.SessionReference == partition.ApprovalSessionReference
        && grant.OperatorKey == partition.Key.OperatorKey && grant.ServerBaseUri == partition.Key.ServerBaseUri;

    internal static bool HasUnmixedIdentity(HttpContext context) => context.User.Identity?.IsAuthenticated == true
        && !context.Request.Headers.ContainsKey("Authorization") && !context.Request.Headers.ContainsKey("X-API-Key");

    private static string CookieName(string profileBinding, ConsoleServerAuthority authority) =>
        CookiePrefix + profileBinding + "." + authority;

    private static string Audience(HttpContext context) =>
        context.Request.Scheme + "://" + context.Request.Host + context.Request.PathBase;
}

internal sealed record ConsoleApprovalBrowserTarget
{
    public required ConsoleEnvironmentProfile SelectedProfile { get; init; }
    public required ConsoleEnvironmentProfile AuthorityProfile { get; init; }
    public required ConsoleServerAuthority Authority { get; init; }
    public required ConsoleServerSessionPartition Partition { get; init; }
}

internal sealed record ConsoleApprovalBrowserGrant
{
    public required string OperatorKey { get; init; }
    public required string ProfileBinding { get; init; }
    public required string AuthorityBinding { get; init; }
    public required string ServerBaseUri { get; init; }
    public required ConsoleServerAuthority Authority { get; init; }
    public required string SessionReference { get; init; }
    public required string Audience { get; init; }
    public required DateTimeOffset ExpiresAt { get; init; }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ConsoleApprovalBrowserGrant))]
internal sealed partial class ConsoleApprovalBrowserJsonContext : JsonSerializerContext;
