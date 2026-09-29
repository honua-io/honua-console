using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Bunit;
using Honua.Console.Contracts;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

/// <summary>Exercises the rendered controls through the production operation and HTTP client.
/// Only the HTTP transport is stubbed; assertions inspect the serialized server requests.</summary>
public sealed class OperateImportReplacementHttpTests
{
    [Fact]
    public void Create_DefaultBatchNeverAuthorizesReplacement()
    {
        using var fixture = new ImportFixture();
        fixture.Page.Find("[data-select-all]").Click();
        fixture.Import();

        Assert.Collection(fixture.Requests,
            request => AssertTarget(request, 1, false),
            request => AssertTarget(request, 2, false));
        Assert.Empty(fixture.Page.FindAll("[data-replace-confirmation]"));
        Assert.All(fixture.Page.FindAll("[data-import-job]"), row => Assert.Contains("Queued", row.TextContent));
    }

    [Fact]
    public void ExistingTargetConflict_IsReportedWithoutEscalatingOrRetrying()
    {
        using var fixture = new ImportFixture(HttpStatusCode.Conflict, "public.imp_roads_roads_1 already exists");
        fixture.Select(0);
        fixture.Import();

        AssertTarget(Assert.Single(fixture.Requests), 1, false);
        Assert.Contains("already exists", fixture.Page.Find("[data-import-job]").TextContent);
        Assert.Empty(fixture.Page.FindAll("[data-replace-confirmation]"));
    }

    [Fact]
    public void QueuedExistingTargetConflict_IsReportedFromJobWithoutReplacementRetry()
    {
        using var fixture = new ImportFixture(jobFailure: "public.imp_roads_roads_1 already exists");
        fixture.Select(0);
        fixture.Import();

        fixture.Page.WaitForAssertion(() =>
            Assert.Contains("already exists", fixture.Page.Find("[data-import-job]").TextContent), TimeSpan.FromSeconds(5));
        AssertTarget(Assert.Single(fixture.Requests), 1, false);
        Assert.Equal("Failed", fixture.Page.Find("[data-import-job]").GetAttribute("data-import-status"));
    }

    [Fact]
    public void DeselectedTarget_IsSkippedWhileOtherTargetIsCreated()
    {
        using var fixture = new ImportFixture();
        fixture.Page.Find("[data-select-all]").Click();
        fixture.Replace(0);
        fixture.Page.FindAll("[data-layer-checkbox]")[0].Change(false);
        fixture.Import();

        AssertTarget(Assert.Single(fixture.Requests), 2, false);
    }

    [Fact]
    public void SingleReplacement_NamesExactDestinationAndConsumesConsentOnSuccess()
    {
        using var fixture = new ImportFixture();
        fixture.Select(0);
        fixture.Replace(0);
        fixture.Import();

        Assert.Empty(fixture.Requests);
        var confirmation = fixture.Page.Find("[data-replace-confirmation]");
        Assert.Contains("Replace 1 of 1", confirmation.TextContent);
        Assert.Equal("public.imp_roads_roads_1", Assert.Single(confirmation.QuerySelectorAll("li")).TextContent);
        fixture.Confirm();
        AssertTarget(Assert.Single(fixture.Requests), 1, true);

        fixture.Import();
        AssertTarget(fixture.Requests[1], 1, false);
    }

    [Fact]
    public void MixedBatch_OnlyNamedConfirmedTargetMayBeReplaced()
    {
        using var fixture = new ImportFixture();
        fixture.Page.Find("[data-select-all]").Click();
        fixture.Replace(1);
        fixture.Import();

        Assert.Empty(fixture.Requests);
        var confirmation = fixture.Page.Find("[data-replace-confirmation]");
        Assert.Contains("Replace 1 of 2", confirmation.TextContent);
        Assert.Equal("public.imp_roads_roads_2", Assert.Single(confirmation.QuerySelectorAll("li")).TextContent);
        fixture.Confirm();

        Assert.Collection(fixture.Requests,
            request => AssertTarget(request, 1, false),
            request => AssertTarget(request, 2, true));
    }

    [Fact]
    public void CancelConfirmation_QueuesNothingAndRevokesReplacementChoices()
    {
        using var fixture = new ImportFixture();
        fixture.Select(0);
        fixture.Replace(0);
        fixture.Import();
        fixture.Page.Find("[data-cancel-replacement]").Click();

        Assert.Empty(fixture.Requests);
        Assert.Empty(fixture.Page.FindAll("[data-replace-confirmation]"));
        Assert.False(fixture.Page.Find("[data-replace-target]").HasAttribute("checked"));
        fixture.Import();
        AssertTarget(Assert.Single(fixture.Requests), 1, false);
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable, false)]
    [InlineData(HttpStatusCode.ServiceUnavailable, true)]
    [InlineData(HttpStatusCode.Forbidden, false)]
    public void QueueRejectionOrTransportFailure_PreservesBatchIntentAndRevokesConsentForRetry(
        HttpStatusCode status, bool transportFailure)
    {
        using var fixture = new ImportFixture(status, "Server rejected import", transportFailure);
        fixture.Page.Find("[data-select-all]").Click();
        fixture.Replace(0);
        fixture.Import();
        fixture.Confirm();

        Assert.Collection(fixture.Requests,
            request => AssertTarget(request, 1, true),
            request => AssertTarget(request, 2, false));
        Assert.All(fixture.Page.FindAll("[data-import-job]"), row => Assert.Contains("Failed to queue", row.TextContent));

        fixture.Import();
        Assert.Equal(4, fixture.Requests.Count);
        AssertTarget(fixture.Requests[2], 1, false);
        AssertTarget(fixture.Requests[3], 2, false);
    }

    [Fact]
    public void SelectionChange_InvalidatesPendingConfirmation()
    {
        using var fixture = new ImportFixture();
        fixture.Select(0);
        fixture.Replace(0);
        fixture.Import();
        fixture.Page.Find("[data-select-all]").Click();

        Assert.Empty(fixture.Requests);
        Assert.Empty(fixture.Page.FindAll("[data-replace-confirmation]"));
        fixture.Import();
        Assert.Contains("Replace 1 of 2", fixture.Page.Find("[data-replace-confirmation]").TextContent);
        Assert.Empty(fixture.Requests);
    }

    private static void AssertTarget(JsonElement request, int layerId, bool overwrite)
    {
        Assert.Equal("https://source.example/FeatureServer", request.GetProperty("serviceUrl").GetString());
        Assert.Equal(layerId, request.GetProperty("layerId").GetInt32());
        Assert.Equal($"imp_roads_roads_{layerId}", request.GetProperty("tableName").GetString());
        Assert.Equal("public", request.GetProperty("targetSchema").GetString());
        Assert.Equal(overwrite, request.GetProperty("overwriteExisting").GetBoolean());
    }

    private sealed class ImportFixture : IDisposable
    {
        private readonly BunitContext _context = new BunitContext().AddConsoleNotifications();
        private readonly HttpClient _http;
        private readonly HonuaAdminOperateHttpClient _client;

        public ImportFixture(HttpStatusCode status = HttpStatusCode.Accepted, string detail = "", bool transportFailure = false, string? jobFailure = null)
        {
            var address = new Uri("https://server.example/");
            _http = new HttpClient(new ImportHandler(Requests, status, detail, transportFailure, jobFailure)) { BaseAddress = address };
            _client = new HonuaAdminOperateHttpClient(_http, new HonuaAdminOperateClientOptions(address, "test-key"));
            _context.Services.AddSingleton<IConsoleServiceImportOperation>(new HonuaServerConsoleServiceImportOperation(_client));
            Page = _context.Render<OperateImportServicePage>();
            Page.Find("input[placeholder^='https://']").Input("https://source.example/FeatureServer");
            Page.Find("button.console-button").Click();
            Page.WaitForAssertion(() => Assert.Equal(2, Page.FindAll("[data-layer-checkbox]").Count));
        }

        public List<JsonElement> Requests { get; } = [];
        public IRenderedComponent<OperateImportServicePage> Page { get; }
        public void Select(int index) => Page.FindAll("[data-layer-checkbox]")[index].Change(true);
        public void Replace(int index) => Page.FindAll("[data-replace-target]")[index].Change(true);
        public void Import() => Page.Find("[data-import-selected]").Click();
        public void Confirm() => Page.Find("[data-confirm-replacement]").Click();

        public void Dispose()
        {
            _context.Dispose();
            _client.Dispose();
            _http.Dispose();
        }
    }

    private sealed class ImportHandler(List<JsonElement> requests, HttpStatusCode status, string detail, bool transportFailure, string? jobFailure) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal("test-key", Assert.Single(request.Headers.GetValues("X-API-Key")));
            if (request.RequestUri!.AbsolutePath == "/api/v1/admin/external-services/discover")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new
                    {
                        serviceName = "Roads",
                        serviceType = "FeatureServer",
                        normalizedUrl = "https://source.example/FeatureServer",
                        candidates = new[] { new { layerId = 1, name = "Roads" }, new { layerId = 2, name = "Roads" } },
                    }),
                };
            }

            if (request.Method == HttpMethod.Get)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new { jobId = "job-1", status = jobFailure is null ? 6 : 7, errorMessage = jobFailure }),
                };
            }

            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/api/v1/admin/import/geoservices/start", request.RequestUri.AbsolutePath);
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            requests.Add(body.RootElement.Clone());
            if (transportFailure)
            {
                throw new HttpRequestException("Connection reset while queueing import");
            }

            return new HttpResponseMessage(status)
            {
                Content = status == HttpStatusCode.Accepted
                    ? JsonContent.Create(new { jobId = $"job-{requests.Count}", status = 1 })
                    : JsonContent.Create(new { message = detail }),
            };
        }
    }
}
