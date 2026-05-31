using System.Net;
using System.Text;
using System.Text.Json;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;
using Honua.Console.Shell.Validation;

namespace Honua.Console.Native.Core.Tests;

/// <summary>
/// Coverage for the Wave-2 server-diagnostic surfacing: <see cref="HttpStudioPackageLifecycleClient"/> now
/// parses the <c>StudioValidationSummary.diagnostics[]</c> body on a non-2xx response and carries them on the
/// endpoint issue, and the map data source maps each diagnostic (JSON-Pointer addressed) onto a console field
/// key so it can surface inline next to the offending layer. Previously this body was discarded.
/// </summary>
public sealed class StudioPackageLifecycleDiagnosticsTests
{
    private static readonly Uri BaseUri = new("https://server.example");

    private static string ValidationBody() => JsonSerializer.Serialize(new
    {
        success = false,
        data = new
        {
            status = "invalid",
            diagnostics = new[]
            {
                new
                {
                    code = "studio.binding.ref.required",
                    severity = "error",
                    path = "/body/layers/0/sourceRef",
                    message = "Layer 0 must bind a source reference.",
                },
                new
                {
                    code = "studio.map.initial-view.bbox.order",
                    severity = "blocker",
                    path = "/body/initialExtent",
                    message = "Initial extent is inverted.",
                },
            },
        },
    });

    [Fact]
    public async Task NonSuccessValidationBody_IsParsedAndCarriedOnTheIssue()
    {
        var handler = new StubHandler(HttpStatusCode.UnprocessableEntity, ValidationBody());
        using var httpClient = new HttpClient(handler) { BaseAddress = BaseUri };
        var client = new HttpStudioPackageLifecycleClient(
            httpClient,
            new StudioPackageLifecycleClientOptions(BaseUri));

        var result = await client.ValidatePackageDraftAsync(Guid.NewGuid());

        Assert.NotNull(result.Issue);
        Assert.Equal(2, result.Issue!.Diagnostics.Count);
        Assert.Contains(result.Issue.Diagnostics, d => d.Path == "/body/layers/0/sourceRef");
        Assert.Contains(result.Issue.Diagnostics, d => d.Severity == StudioPackageDiagnosticSeverity.Blocker);
    }

    [Fact]
    public async Task SaveDraft_OnValidationRejection_BindsDiagnosticsOntoFieldKeys()
    {
        var handler = new StubHandler(HttpStatusCode.UnprocessableEntity, ValidationBody());
        using var httpClient = new HttpClient(handler) { BaseAddress = BaseUri };
        var client = new HttpStudioPackageLifecycleClient(
            httpClient,
            new StudioPackageLifecycleClientOptions(BaseUri));
        var source = new HonuaServerStudioMapPackageDataSource(client);

        var state = new StudioMapEditorState { Title = "Public works", Basemap = "basemap:streets", InitialExtent = "0,0,1,1" };
        state.Layers.Add(new StudioMapLayerEditor { SourceRef = "content:hydrants@v12" });

        var result = await source.SaveDraftAsync(state);

        Assert.False(result.Succeeded);
        Assert.NotNull(result.FieldErrors);
        Assert.Contains(result.FieldErrors!, e => e.FieldKey == StudioMapFieldKeys.LayerSourceRef(0));
        Assert.Contains(result.FieldErrors!, e => e.FieldKey == StudioMapFieldKeys.InitialExtent);
    }

    [Fact]
    public async Task NonSuccessWithoutValidationBody_YieldsNoDiagnostics()
    {
        var handler = new StubHandler(HttpStatusCode.Forbidden, """{"success":false,"message":"nope"}""");
        using var httpClient = new HttpClient(handler) { BaseAddress = BaseUri };
        var client = new HttpStudioPackageLifecycleClient(
            httpClient,
            new StudioPackageLifecycleClientOptions(BaseUri));

        var result = await client.ValidatePackageDraftAsync(Guid.NewGuid());

        Assert.NotNull(result.Issue);
        Assert.Empty(result.Issue!.Diagnostics);
    }

    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
    }
}
