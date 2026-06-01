using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Honua.Console.Contracts;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Docker-free coverage for <see cref="HonuaConsoleShareHttpClient"/> transport + contract semantics against
/// the honua-server Console Share access API (honua-server#1215). Asserts: the {success,data} envelope is
/// unwrapped to the typed projection; requests target the exact console share routes and carry the admin
/// API key; the public-link mint POST serializes the request body and surfaces the one-time token value;
/// a 409 surfaces a typed Conflict issue carrying the server's blocking-dependency message; 404/403 map to
/// Unsupported/Missing-permission; caller cancellation propagates while an HttpClient timeout is Unavailable.
/// </summary>
public sealed class ShareAccessHttpClientTests
{
    private static readonly Uri BaseAddress = new("https://honua.test");
    private const string ApiKey = "admin-key-xyz";

    [Fact]
    public async Task GetShare_OnSuccess_UnwrapsEnvelopeAndTargetsConsoleRouteWithApiKey()
    {
        string? path = null;
        string? apiKey = null;
        var projection = new HonuaConsoleShareProjection
        {
            ItemId = "item-1",
            ItemName = "storm-layer",
            ItemTitle = "Storm Layer",
            ItemType = "layer",
            AccessTier = HonuaConsoleShareAccessTiers.PublicIndexed,
            OpenDataEligible = true,
            AnonymousEligible = true,
            PublicLinkEnabled = true,
            CallerPermissions = ["view", "share", "embed", "administer"],
            PublicLinkTokens =
            [
                new HonuaConsolePublicLinkToken { TokenId = "tok-1", ItemId = "item-1", IsExpired = false }
            ]
        };
        using var client = CreateClient(new RecordingHandler(request =>
        {
            path = request.RequestUri?.AbsolutePath;
            apiKey = request.Headers.TryGetValues("X-API-Key", out var v) ? v.FirstOrDefault() : null;
            return Ok(projection);
        }));

        var result = await client.GetShareAsync("item-1");

        Assert.Null(result.Issue);
        Assert.Equal("/api/v1/console/content/item-1/share", path);
        Assert.Equal(ApiKey, apiKey);
        Assert.Equal(HonuaConsoleShareAccessTiers.PublicIndexed, result.Data!.AccessTier);
        Assert.True(result.Data.OpenDataEligible);
        Assert.Contains("administer", result.Data.CallerPermissions);
    }

    [Fact]
    public async Task UpdateAccessTier_SerializesBodyAndPutsToAccessRoute()
    {
        string? path = null;
        HttpMethod? method = null;
        JsonElement body = default;
        using var client = CreateClient(new RecordingHandler(request =>
        {
            path = request.RequestUri?.AbsolutePath;
            method = request.Method;
            body = ReadBody(request);
            return Ok(new HonuaConsoleShareProjection { ItemId = "item-1", ItemName = "n", ItemType = "layer", AccessTier = HonuaConsoleShareAccessTiers.PublicLink });
        }));

        var result = await client.UpdateAccessTierAsync(
            "item-1",
            new HonuaUpdateShareAccessRequest { AccessTier = HonuaConsoleShareAccessTiers.PublicLink, AllowDependencyConflicts = true });

        Assert.Null(result.Issue);
        Assert.Equal(HttpMethod.Put, method);
        Assert.Equal("/api/v1/console/content/item-1/share/access", path);
        Assert.Equal("public-link", body.GetProperty("accessTier").GetString());
        Assert.True(body.GetProperty("allowDependencyConflicts").GetBoolean());
    }

    [Fact]
    public async Task MintPublicLink_PostsExpiryAndSurfacesOneTimeTokenValue()
    {
        var expires = DateTimeOffset.UtcNow.AddDays(7);
        JsonElement body = default;
        using var client = CreateClient(new RecordingHandler(request =>
        {
            body = ReadBody(request);
            return Ok(new HonuaConsolePublicLinkToken
            {
                TokenId = "tok-9",
                Token = "opaque-secret-value",
                ItemId = "item-1",
                ExpiresAt = expires
            });
        }));

        var result = await client.MintPublicLinkAsync("item-1", new HonuaMintPublicLinkRequest { ExpiresAt = expires });

        Assert.Null(result.Issue);
        Assert.Equal("opaque-secret-value", result.Data!.Token);
        Assert.Equal("tok-9", result.Data.TokenId);
        Assert.True(body.TryGetProperty("expiresAt", out _));
    }

    [Fact]
    public async Task RevokePublicLink_DeletesTokenByIdRoute()
    {
        string? path = null;
        HttpMethod? method = null;
        using var client = CreateClient(new RecordingHandler(request =>
        {
            path = request.RequestUri?.AbsolutePath;
            method = request.Method;
            return Ok(new HonuaConsoleShareCommandAck { Message = "Public-link token revoked." });
        }));

        var result = await client.RevokePublicLinkAsync("item-1", "tok-9");

        Assert.Null(result.Issue);
        Assert.Equal(HttpMethod.Delete, method);
        Assert.Equal("/api/v1/console/content/item-1/share/link/tok-9", path);
    }

    [Fact]
    public async Task RevokePublicLink_WhenServerReturnsSuccessWithNullData_IsTreatedAsAck()
    {
        // The real honua-server revoke returns {success:true, data:null, message:"Public-link token revoked."}.
        // The client must surface this as a successful ack, not a false Unavailable issue (regression guard for
        // the Share token round-trip, where a misreported revoke broke the revoke→denied negative companion).
        using var client = CreateClient(new RecordingHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(HonuaAdminApiResponseLike<object?>(true, null, "Public-link token revoked."))
            }));

        var result = await client.RevokePublicLinkAsync("item-1", "tok-9");

        Assert.Null(result.Issue);
        Assert.NotNull(result.Data);
        Assert.Equal("Public-link token revoked.", result.Data!.Message);
    }

    [Fact]
    public async Task SetEmbed_OnConflict_SurfacesTypedConflictWithServerMessage()
    {
        using var client = CreateClient(new RecordingHandler(_ =>
        {
            var closure = new HonuaConsoleShareDependencyClosure
            {
                ItemId = "item-1",
                TargetTier = HonuaConsoleShareAccessTiers.PublicLink,
                IsCompatible = false,
                Conflicts =
                [
                    new HonuaConsoleShareDependencyConflict { ItemId = "dep-1", ItemName = "Private source", BlockingReason = "item visibility is 'personal'" }
                ]
            };
            var envelope = HonuaAdminApiResponseLike(false, closure, "Dependency closure is not shareable for embedding.");
            return new HttpResponseMessage(HttpStatusCode.Conflict) { Content = JsonContent.Create(envelope) };
        }));

        var result = await client.SetEmbedAsync("item-1", new HonuaSetEmbedRequest { Enabled = true, Audience = HonuaConsoleEmbedAudiences.Map });

        Assert.Null(result.Data);
        Assert.NotNull(result.Issue);
        Assert.Equal("Conflict", result.Issue!.State);
        Assert.Equal(409, result.Issue.StatusCode);
        Assert.Contains("not shareable", result.Issue.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetShare_WhenNotFound_MapsToUnsupported()
    {
        using var client = CreateClient(new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound)));

        var result = await client.GetShareAsync("missing");

        Assert.NotNull(result.Issue);
        Assert.Equal("Unsupported", result.Issue!.State);
        Assert.Equal(404, result.Issue.StatusCode);
    }

    [Fact]
    public async Task GetShare_WhenForbidden_MapsToMissingPermission()
    {
        using var client = CreateClient(new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden)));

        var result = await client.GetShareAsync("item-1");

        Assert.NotNull(result.Issue);
        Assert.Equal("Missing permission", result.Issue!.State);
        Assert.Equal(403, result.Issue.StatusCode);
    }

    [Fact]
    public async Task GetShare_WhenCallerCancels_PropagatesCancellation()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        using var client = CreateClient(new BlockUntilCancelledHandler());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetShareAsync("item-1", cts.Token));
    }

    [Fact]
    public async Task GetShare_OnHttpClientTimeout_ReturnsUnavailable()
    {
        using var client = CreateClient(new BlockUntilCancelledHandler(), TimeSpan.FromMilliseconds(50));

        var result = await client.GetShareAsync("item-1", CancellationToken.None);

        Assert.NotNull(result.Issue);
        Assert.Equal("Unavailable", result.Issue!.State);
    }

    private static HttpResponseMessage Ok<T>(T data) =>
        new(HttpStatusCode.OK) { Content = JsonContent.Create(HonuaAdminApiResponseLike(true, data, null)) };

    private static object HonuaAdminApiResponseLike<T>(bool success, T data, string? message) =>
        new { success, data, message, timestamp = DateTimeOffset.UtcNow };

    private static JsonElement ReadBody(HttpRequestMessage request)
    {
        var json = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    private static HonuaConsoleShareHttpClient CreateClient(HttpMessageHandler handler, TimeSpan? timeout = null)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = BaseAddress };
        if (timeout is { } value)
        {
            httpClient.Timeout = value;
        }

        return new HonuaConsoleShareHttpClient(httpClient, new HonuaConsoleShareClientOptions(BaseAddress, ApiKey));
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _factory;

        public RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> factory) => _factory = factory;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // Materialize the body before the request is disposed so assertions can read it.
            if (request.Content is not null)
            {
                _ = request.Content.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult();
            }

            return Task.FromResult(_factory(request));
        }
    }

    private sealed class BlockUntilCancelledHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
}
