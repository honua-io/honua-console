using System.Net;
using System.Text;
using System.Text.Json;
using Honua.Console.Contracts;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Docker-free coverage for <see cref="HonuaConsoleContentHttpClient"/> transport semantics against the
/// honua-server Console metadata v2 content + RBAC contract (honua-server#1162, issue #7). Caller-requested
/// cancellation must propagate (it cancels the calling operation rather than masquerading as an Unavailable
/// endpoint issue), while an HttpClient timeout still surfaces as Unavailable. The shared
/// {success,data,message} envelope, status mapping, admin X-API-Key header, list query composition, and the
/// action-check / create POST bodies are asserted. Mirrors the content publication client contract test.
/// </summary>
public sealed class ConsoleContentHttpClientTests
{
    private static readonly Uri BaseAddress = new("https://honua.test");

    [Fact]
    public async Task Get_WhenCallerCancels_PropagatesCancellation()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        using var client = CreateClient(new BlockUntilCancelledHandler());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetAsync("item-1", cts.Token));
    }

    [Fact]
    public async Task Get_OnHttpClientTimeout_ReturnsUnavailable()
    {
        using var client = CreateClient(new BlockUntilCancelledHandler(), TimeSpan.FromMilliseconds(50));

        var result = await client.GetAsync("item-1", CancellationToken.None);

        Assert.NotNull(result.Issue);
        Assert.Equal("Unavailable", result.Issue!.State);
    }

    [Fact]
    public async Task Get_WhenNotFound_MapsToUnsupportedIssue()
    {
        using var client = CreateClient(new StaticResponseHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound)));

        var result = await client.GetAsync("ghost");

        Assert.Null(result.Data);
        Assert.Equal("Unsupported", result.Issue!.State);
        Assert.Equal(404, result.Issue.StatusCode);
    }

    [Fact]
    public async Task Get_WhenForbidden_MapsToMissingPermission()
    {
        using var client = CreateClient(new StaticResponseHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden)));

        var result = await client.GetAsync("locked");

        Assert.Equal("Missing permission", result.Issue!.State);
        Assert.Equal(403, result.Issue.StatusCode);
    }

    [Fact]
    public async Task Get_OnSuccess_UnwrapsEnvelopeAndSendsAdminKey()
    {
        string? apiKey = null;
        using var client = CreateClient(
            new StaticResponseHandler(request =>
            {
                apiKey = request.Headers.TryGetValues("X-API-Key", out var values) ? values.Single() : null;
                return EnvelopeResponse(new HonuaConsoleContentItem
                {
                    Id = "item-1",
                    Name = "coastal",
                    Title = "Coastal Flood Service",
                    ItemType = HonuaConsoleContentItemTypes.Service,
                    Visibility = HonuaConsoleVisibilities.Public,
                    Actions = ["view", "embed"]
                });
            }),
            apiKey: "admin-secret");

        var result = await client.GetAsync("item-1");

        Assert.Null(result.Issue);
        Assert.Equal("Coastal Flood Service", result.Data!.Title);
        Assert.Equal("admin-secret", apiKey);
    }

    [Fact]
    public async Task List_ComposesFilterQueryString()
    {
        string? query = null;
        using var client = CreateClient(new StaticResponseHandler(request =>
        {
            query = request.RequestUri?.Query;
            return EnvelopeResponse(new HonuaConsoleContentListResponse { Total = 0 });
        }));

        await client.ListAsync(new HonuaConsoleContentListQuery
        {
            ItemType = HonuaConsoleContentItemTypes.SavedMap,
            Visibility = HonuaConsoleVisibilities.Organization,
            Owner = "resilience",
            SearchTerm = "flood",
            Limit = 50
        });

        Assert.Contains("itemType=saved-map", query, StringComparison.Ordinal);
        Assert.Contains("visibility=organization", query, StringComparison.Ordinal);
        Assert.Contains("owner=resilience", query, StringComparison.Ordinal);
        Assert.Contains("q=flood", query, StringComparison.Ordinal);
        Assert.Contains("limit=50", query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CheckActions_PostsTargetsAndActionsBody()
    {
        string? body = null;
        using var client = CreateClient(new StaticResponseHandler(request =>
        {
            body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return EnvelopeResponse(new HonuaConsoleActionCheckResponse
            {
                Results = [new HonuaConsoleActionCheckResult { ItemId = "item-1", Allowed = ["view"], Denied = ["edit"] }]
            });
        }));

        var result = await client.CheckActionsAsync(new HonuaConsoleActionCheckRequest
        {
            Targets = [new HonuaConsoleActionCheckTarget { ItemId = "item-1" }],
            Actions = ["view", "edit"]
        });

        Assert.Contains("\"itemId\":\"item-1\"", body, StringComparison.Ordinal);
        Assert.Contains("view", body, StringComparison.Ordinal);
        var entry = Assert.Single(result.Data!.Results);
        Assert.Contains("view", entry.Allowed);
        Assert.Contains("edit", entry.Denied);
    }

    [Fact]
    public async Task Create_PostsToContentRootAndUnwrapsCreatedItem()
    {
        string? path = null;
        HttpMethod? method = null;
        using var client = CreateClient(new StaticResponseHandler(request =>
        {
            path = request.RequestUri?.AbsolutePath;
            method = request.Method;
            return EnvelopeResponse(
                new HonuaConsoleContentItem
                {
                    Id = "new-1",
                    Name = "seeded",
                    ItemType = HonuaConsoleContentItemTypes.Service,
                    Visibility = HonuaConsoleVisibilities.Public,
                    Actions = ["view"]
                },
                HttpStatusCode.Created);
        }));

        var result = await client.CreateAsync(new HonuaCreateConsoleContentItemRequest
        {
            Name = "seeded",
            ItemType = HonuaConsoleContentItemTypes.Service,
            Visibility = HonuaConsoleVisibilities.Public
        });

        Assert.Equal(HttpMethod.Post, method);
        Assert.Equal("/api/v1/console/content/", path);
        Assert.Equal("new-1", result.Data!.Id);
    }

    private static HonuaConsoleContentHttpClient CreateClient(HttpMessageHandler handler, TimeSpan? timeout = null, string? apiKey = null)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = BaseAddress };
        if (timeout is { } value)
        {
            httpClient.Timeout = value;
        }

        return new HonuaConsoleContentHttpClient(httpClient, new HonuaConsoleContentClientOptions(BaseAddress, apiKey));
    }

    private static HttpResponseMessage EnvelopeResponse<T>(T data, HttpStatusCode status = HttpStatusCode.OK)
    {
        var json = JsonSerializer.Serialize(new EnvelopeDto<T>(true, data, null));
        return new HttpResponseMessage(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private sealed record EnvelopeDto<T>(bool success, T data, string? message);

    private sealed class BlockUntilCancelledHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    private sealed class StaticResponseHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responseFactory(request));
    }
}
