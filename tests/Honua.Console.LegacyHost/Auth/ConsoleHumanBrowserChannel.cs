using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;
using Microsoft.AspNetCore.DataProtection;

namespace Honua.Console.Web.Auth;

internal sealed class ConsoleApprovalBrowserChannel(
    IDataProtectionProvider protection, IHttpContextAccessor requests, TimeProvider clock,
    ILogger<ConsoleApprovalBrowserChannel> logger)
    : ConsoleHumanBrowserChannel(protection, requests, clock, logger)
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);
    protected override string ProtectionPurpose => "Honua.Console.ApprovalBrowser.v1";
    protected override string CookiePrefix => "__Host-HonuaApproval.";
    protected override string? SessionReference(ConsoleServerSessionPartition partition) => partition.ApprovalSessionReference;
    protected override void ReplaceSessionReference(ConsoleServerSessionPartition partition, string reference)
    {
        partition.ApprovalSessionReference = reference;
        partition.ProposalReviews.Clear();
    }
    protected override DateTimeOffset? GrantExpiry(ConsoleHumanBrowserTarget target) => Clock.GetUtcNow() + Lifetime;
    protected override bool HasAllowedExpiry(ConsoleHumanBrowserGrant grant) => grant.ExpiresAt <= Clock.GetUtcNow() + Lifetime;
}

internal sealed class ConsoleStudioEditBrowserChannel(
    IDataProtectionProvider protection, IHttpContextAccessor requests, TimeProvider clock,
    ILogger<ConsoleStudioEditBrowserChannel> logger)
    : ConsoleHumanBrowserChannel(protection, requests, clock, logger)
{
    protected override string ProtectionPurpose => "Honua.Console.StudioEditBrowser.v1";
    protected override string CookiePrefix => "__Host-HonuaStudioEdit.";
    protected override string? SessionReference(ConsoleServerSessionPartition partition) => partition.StudioEditSessionReference;
    protected override void ReplaceSessionReference(ConsoleServerSessionPartition partition, string reference) =>
        partition.StudioEditSessionReference = reference;
    protected override DateTimeOffset? GrantExpiry(ConsoleHumanBrowserTarget target) =>
        target.Authority == ConsoleServerAuthority.Serving ? target.Partition.AuthenticatedSessionExpiresAt : null;
    protected override string? CookieBinding(ConsoleHumanBrowserTarget target) => SessionCookieBinding(target.Partition, target.AuthorityProfile);
    protected override bool AuthorizesGrant(ConsoleHumanBrowserGrant grant, ConsoleHumanBrowserTarget target) =>
        grant.ExpiresAt <= target.Partition.AuthenticatedSessionExpiresAt
        && grant.SessionCookieBinding == CookieBinding(target);
}

internal abstract class ConsoleHumanBrowserChannel(
    IDataProtectionProvider protection, IHttpContextAccessor requests, TimeProvider clock,
    ILogger logger)
{
    protected TimeProvider Clock { get; } = clock;
    protected abstract string ProtectionPurpose { get; }
    protected abstract string CookiePrefix { get; }
    protected abstract string? SessionReference(ConsoleServerSessionPartition partition);
    protected abstract void ReplaceSessionReference(ConsoleServerSessionPartition partition, string reference);
    protected abstract DateTimeOffset? GrantExpiry(ConsoleHumanBrowserTarget target);
    protected virtual string? CookieBinding(ConsoleHumanBrowserTarget target) => null;
    protected virtual bool AuthorizesGrant(ConsoleHumanBrowserGrant grant, ConsoleHumanBrowserTarget target) => true;
    protected virtual bool HasAllowedExpiry(ConsoleHumanBrowserGrant grant) => true;
    private IDataProtector Protector => protection.CreateProtector(ProtectionPurpose);

    public void CompleteCallback(ConsoleHumanBrowserTarget target)
    {
        var sessionReference = Guid.NewGuid().ToString("N");
        ReplaceSessionReference(target.Partition, sessionReference);
        var context = requests.HttpContext;
        if (context is null || !context.Request.IsHttps || !HasUnmixedIdentity(context)
            || ConsoleOperatorContext.ResolveKey(context.User) != target.Partition.Key.OperatorKey)
        {
            logger.LogWarning("The completed sign-in could not establish the human browser channel. Use HTTPS and a matching interactive Console identity without bearer or key credentials.");
            return;
        }
        var expiresAt = GrantExpiry(target);
        if (expiresAt is null || expiresAt <= Clock.GetUtcNow())
        {
            logger.LogWarning("The completed sign-in has no eligible validated session expiry for this human browser channel.");
            return;
        }
        var grant = new ConsoleHumanBrowserGrant
        {
            OperatorKey = target.Partition.Key.OperatorKey,
            ProfileBinding = ConsoleServerProfileBinding.Capture(target.SelectedProfile),
            AuthorityBinding = ConsoleServerProfileBinding.Capture(target.AuthorityProfile),
            ServerBaseUri = target.AuthorityProfile.ServerBaseUri.AbsoluteUri,
            Authority = target.Authority,
            SessionReference = sessionReference,
            Audience = Audience(context),
            ExpiresAt = expiresAt.Value,
            SessionCookieBinding = CookieBinding(target)
        };
        var value = Protector.Protect(JsonSerializer.Serialize(grant,
            ConsoleHumanBrowserJsonContext.Default.ConsoleHumanBrowserGrant));
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

    public bool Authorizes(ConsoleHumanBrowserTarget target)
    {
        var grant = ReadGrant(ConsoleServerProfileBinding.Capture(target.SelectedProfile), target.Authority);
        return grant is not null && MatchesPartition(grant, target.Partition)
            && grant.AuthorityBinding == ConsoleServerProfileBinding.Capture(target.AuthorityProfile)
            && AuthorizesGrant(grant, target);
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

    private ConsoleHumanBrowserGrant? ReadGrant(string profileBinding, ConsoleServerAuthority authority)
    {
        var context = requests.HttpContext;
        if (context is null || !HasUnmixedIdentity(context)
            || !context.Request.Cookies.TryGetValue(CookieName(profileBinding, authority), out var value)
            || value.Length > 8192) return null;
        try
        {
            var grant = JsonSerializer.Deserialize(Protector.Unprotect(value),
                ConsoleHumanBrowserJsonContext.Default.ConsoleHumanBrowserGrant);
            return grant is not null && grant.ProfileBinding == profileBinding && grant.Authority == authority
                && grant.OperatorKey == ConsoleOperatorContext.ResolveKey(context.User)
                && grant.Audience == Audience(context) && grant.ExpiresAt > Clock.GetUtcNow()
                && HasAllowedExpiry(grant) ? grant : null;
        }
        catch (Exception error) when (error is CryptographicException or JsonException or FormatException)
        {
            return null;
        }
    }

    private bool MatchesPartition(ConsoleHumanBrowserGrant grant, ConsoleServerSessionPartition partition) =>
        !string.IsNullOrEmpty(grant.SessionReference) && grant.SessionReference ==
            SessionReference(partition)
        && grant.OperatorKey == partition.Key.OperatorKey && grant.ServerBaseUri == partition.Key.ServerBaseUri;

    protected static string SessionCookieBinding(ConsoleServerSessionPartition partition, ConsoleEnvironmentProfile profile) =>
        Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(partition.Cookies.GetCookieHeader(profile.ServerBaseUri))));

    internal static bool HasUnmixedIdentity(HttpContext context) => context.User.Identity?.IsAuthenticated == true
        && !context.Request.Headers.ContainsKey("Authorization") && !context.Request.Headers.ContainsKey("X-API-Key");

    private string CookieName(string profileBinding, ConsoleServerAuthority authority) =>
        CookiePrefix + profileBinding + "." + authority;

    private static string Audience(HttpContext context) =>
        context.Request.Scheme + "://" + context.Request.Host + context.Request.PathBase;
}

internal sealed record ConsoleHumanBrowserTarget
{
    public required ConsoleEnvironmentProfile SelectedProfile { get; init; }
    public required ConsoleEnvironmentProfile AuthorityProfile { get; init; }
    public required ConsoleServerAuthority Authority { get; init; }
    public required ConsoleServerSessionPartition Partition { get; init; }
}

internal sealed record ConsoleHumanBrowserGrant
{
    public required string OperatorKey { get; init; }
    public required string ProfileBinding { get; init; }
    public required string AuthorityBinding { get; init; }
    public required string ServerBaseUri { get; init; }
    public required ConsoleServerAuthority Authority { get; init; }
    public required string SessionReference { get; init; }
    public required string Audience { get; init; }
    public required DateTimeOffset ExpiresAt { get; init; }
    public string? SessionCookieBinding { get; init; }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ConsoleHumanBrowserGrant))]
internal sealed partial class ConsoleHumanBrowserJsonContext : JsonSerializerContext;
