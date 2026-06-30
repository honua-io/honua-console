using System.Net;
using System.Net.Http;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;
using Honua.Console.Web.Auth;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Fail-closed coverage for the broad server-bound chokepoint that ALL Family-A typed honua-server
/// clients funnel through (honua-console#254): the singleton <see cref="HonuaServerBindingHandler"/>
/// reading the operator-partitioned <see cref="OperatorScopedEnvironmentProfileStore"/> /
/// <see cref="OperatorScopedAccountSessionStore"/>.
///
/// The S1 fail-open class this guards is cross-operator credential bleed / silent privilege fallback:
/// on a host serving MANY operators from one process, the handler must forward EACH operator's OWN
/// bearer (and never another operator's), and an unresolved operator must never inherit a different
/// operator's forwarded bearer — it falls back to the documented anonymous/admin-key path rather than
/// impersonating someone. <see cref="ConsoleOperatorScopeTests"/> proves the scoped-seam side
/// (unresolved ⇒ null/deny, concurrent scopes isolated); <see cref="ConsoleOperatorCircuitPartitionTests"/>
/// proves the circuit ambient feeds the partition; this proves the composition at the actual outbound
/// HTTP boundary, so a regression in the partitioning surfaces as a wrong/leaked Authorization header.
/// </summary>
public sealed class ConsoleServerBindingFailClosedTests
{
    [Fact]
    public async Task BindingHandler_ForwardsEachOperatorsOwnBearer_AndNeverAnothers()
    {
        var context = new SwitchableOperatorContext();
        var profiles = new OperatorScopedEnvironmentProfileStore(context);
        var sessions = new OperatorScopedAccountSessionStore(context);

        await BindOperatorAsync(context, profiles, sessions, "operator-a", "https://a.honua.test/", "bearer-A");
        await BindOperatorAsync(context, profiles, sessions, "operator-b", "https://b.honua.test/", "bearer-B");

        // Operator A's outbound call carries A's bearer (and the shared admin key is stripped), retargeted
        // to A's active server.
        context.CurrentOperatorKey = "operator-a";
        var aCall = await SendThroughHandlerAsync(profiles, sessions);
        Assert.Equal("Bearer bearer-A", aCall.Authorization);
        Assert.Null(aCall.ApiKey);
        Assert.Equal("a.honua.test", aCall.Host);

        // Operator B's outbound call carries B's bearer — never A's. This is the cross-operator bleed the
        // multi-operator host must structurally prevent.
        context.CurrentOperatorKey = "operator-b";
        var bCall = await SendThroughHandlerAsync(profiles, sessions);
        Assert.Equal("Bearer bearer-B", bCall.Authorization);
        Assert.Null(bCall.ApiKey);
        Assert.Equal("b.honua.test", bCall.Host);
    }

    [Fact]
    public async Task BindingHandler_UnresolvedOperator_NeverInheritsAnotherOperatorsBearer()
    {
        var context = new SwitchableOperatorContext();
        var profiles = new OperatorScopedEnvironmentProfileStore(context);
        var sessions = new OperatorScopedAccountSessionStore(context);

        // An authenticated operator has a forwardable bearer on record.
        await BindOperatorAsync(context, profiles, sessions, "operator-a", "https://a.honua.test/", "bearer-A");

        // A call made with NO resolved operator (the shared anonymous partition) must not read operator A's
        // active profile or bearer: the anonymous partition is empty, so the handler leaves the request on
        // the configured admin-key fallback (documented public/open-data path) and forwards NO operator
        // bearer. The fail-open this rules out is the unresolved operator silently acquiring A's identity.
        context.CurrentOperatorKey = ConsoleOperatorContext.AnonymousKey;
        var anonCall = await SendThroughHandlerAsync(profiles, sessions);

        Assert.Null(anonCall.Authorization);          // never "Bearer bearer-A"
        Assert.Equal("admin-key", anonCall.ApiKey);   // documented anonymous/admin-key fallback retained
        Assert.Equal("startup.honua.test", anonCall.Host); // not retargeted to A's server (no anon profile)
    }

    [Fact]
    public async Task SessionStore_UnresolvedOperator_FailsClosedOnWrite()
    {
        // Writing an operator credential while no operator is resolved is a fail-closed bug, never a silent
        // write into the shared anonymous partition (honua-console#256 semantics the #254 redesign keeps).
        var context = new SwitchableOperatorContext { CurrentOperatorKey = ConsoleOperatorContext.AnonymousKey };
        var sessions = new OperatorScopedAccountSessionStore(context);

        await Assert.ThrowsAsync<ConsoleOperatorContextUnresolvedException>(async () =>
            await sessions.SaveSessionAsync(new ConsoleAccountSession
            {
                ProfileId = "env-1",
                AccountId = "ghost",
                AccessToken = "leaked-bearer",
            }));
    }

    private static async Task BindOperatorAsync(
        SwitchableOperatorContext context,
        OperatorScopedEnvironmentProfileStore profiles,
        OperatorScopedAccountSessionStore sessions,
        string operatorKey,
        string serverBaseUri,
        string bearer)
    {
        context.CurrentOperatorKey = operatorKey;
        await profiles.UpsertProfileAsync(new ConsoleEnvironmentProfile
        {
            Id = "env-shared",
            DisplayName = operatorKey,
            ServerBaseUri = new Uri(serverBaseUri),
            Account = new ConsoleAccountBinding
            {
                AuthMode = ConsoleAccountAuthMode.AccountRbac,
                AccountId = operatorKey,
            },
        });
        await profiles.ActivateProfileAsync("env-shared");
        await sessions.SaveSessionAsync(new ConsoleAccountSession
        {
            ProfileId = "env-shared",
            AccountId = operatorKey,
            AccessToken = bearer,
        });
    }

    // Drives one outbound request through the real binding handler exactly as a singleton typed client
    // would, capturing the credential/authority that reaches the wire.
    private static async Task<CapturedRequest> SendThroughHandlerAsync(
        IConsoleEnvironmentProfileStore profiles,
        IConsoleAccountSessionStore sessions)
    {
        var capture = new CapturingHandler();
        var handler = new HonuaServerBindingHandler(profiles, sessions) { InnerHandler = capture };
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://startup.honua.test/") };

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/catalog/items");
        // The inner typed client always stamps the shared admin key; the handler must replace it with the
        // operator bearer when one is resolvable for the active operator.
        request.Headers.TryAddWithoutValidation("X-API-Key", "admin-key");

        using var response = await client.SendAsync(request);
        return capture.Captured!;
    }

    private sealed class CapturedRequest
    {
        public string? Authorization { get; init; }
        public string? ApiKey { get; init; }
        public string? Host { get; init; }
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public CapturedRequest? Captured { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Captured = new CapturedRequest
            {
                Authorization = request.Headers.Authorization?.ToString(),
                ApiKey = request.Headers.TryGetValues("X-API-Key", out var values)
                    ? string.Join(",", values)
                    : null,
                Host = request.RequestUri?.Host,
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    // Mirrors the resolution contract of the real ConsoleOperatorContext (HttpContext.User / circuit
    // ambient) with a directly-settable key so a test can place a call in a chosen operator partition.
    // The real concurrent isolation is proven against ConsoleOperatorContext itself in
    // ConsoleOperatorCircuitPartitionTests; here we assert the per-partition credential routing.
    private sealed class SwitchableOperatorContext : IConsoleOperatorContext
    {
        public string CurrentOperatorKey { get; set; } = ConsoleOperatorContext.AnonymousKey;

        public bool HasOperator =>
            !string.Equals(CurrentOperatorKey, ConsoleOperatorContext.AnonymousKey, StringComparison.Ordinal);

        public string RequireOperatorKey() =>
            HasOperator ? CurrentOperatorKey : throw new ConsoleOperatorContextUnresolvedException();
    }
}
