using System.Net;
using System.Net.Http.Json;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Docker-free coverage for the service settings-caps editor (gapb/caps): a new
/// <c>GET/PUT /api/v1/admin/services/{serviceName}/settings-caps</c> client + operation that lets an operator
/// author a service's result/request limits (maxRecordCount, query timeout, attachment caps, supported
/// formats, …). These assert the real route/verb/body the Save mutation issues (over a stubbed admin client),
/// the result mapping, the read-back projection that pre-populates the editor, and — critically — that a
/// non-success / 501-Not-Implemented / transport / missing-binding result is surfaced HONESTLY rather than
/// fabricated into a success (Console Patterns Charter section 11). They run in PR CI (no Docker).
/// </summary>
public sealed class ServiceSettingsCapsEditorTests
{
    private static readonly Uri BaseAddress = new("https://honua.test");

    [Fact]
    public async Task UpdateSettingsCaps_IssuesPutToSettingsCapsRoute_WithCapsBody()
    {
        string? path = null;
        HttpMethod? method = null;
        string? body = null;
        var caps = new HonuaAdminServiceSettingsCapsResponse
        {
            MaxRecordCount = 2000,
            SupportedFormats = ["json", "geojson"],
            DefaultFormat = "json"
        };
        var operation = new HonuaServerServiceConfigurationOperation(CreateClient(request =>
        {
            path = request.RequestUri?.AbsolutePath;
            method = request.Method;
            body = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            return Ok(caps);
        }));

        var result = await operation.UpdateServiceSettingsCapsAsync(new ServiceSettingsCapsCommand
        {
            ServiceName = "svc",
            MaxRecordCount = 2000,
            DefaultRecordCount = 1000,
            MaxFeaturesPerLayer = 5000,
            QueryTimeoutMs = 30000,
            MaxEditsPerTransaction = 250,
            MaxPayloadBytes = 10_485_760,
            SupportedFormats = ["json", "geojson", "pbf"],
            DefaultFormat = "json",
            DefaultTileMatrixSet = "WebMercatorQuad",
            SupportsAttachments = true,
            MaxAttachmentSizeBytes = 5_242_880
        });

        Assert.True(result.Succeeded);
        Assert.Equal("Updated", result.State);
        Assert.Equal(HttpMethod.Put, method);
        // The Save issues the PUT to the dedicated /settings-caps route.
        Assert.Equal("/api/v1/admin/services/svc/settings-caps", path);
        // Every cap reaches the wire body in camelCase.
        Assert.Contains("\"maxRecordCount\":2000", body!, StringComparison.Ordinal);
        Assert.Contains("\"defaultRecordCount\":1000", body!, StringComparison.Ordinal);
        Assert.Contains("\"maxFeaturesPerLayer\":5000", body!, StringComparison.Ordinal);
        Assert.Contains("\"queryTimeoutMs\":30000", body!, StringComparison.Ordinal);
        Assert.Contains("\"maxEditsPerTransaction\":250", body!, StringComparison.Ordinal);
        Assert.Contains("\"maxPayloadBytes\":10485760", body!, StringComparison.Ordinal);
        Assert.Contains("\"supportedFormats\":[\"json\",\"geojson\",\"pbf\"]", body!, StringComparison.Ordinal);
        Assert.Contains("\"defaultFormat\":\"json\"", body!, StringComparison.Ordinal);
        Assert.Contains("\"defaultTileMatrixSet\":\"WebMercatorQuad\"", body!, StringComparison.Ordinal);
        Assert.Contains("\"supportsAttachments\":true", body!, StringComparison.Ordinal);
        Assert.Contains("\"maxAttachmentSizeBytes\":5242880", body!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetSettings_ProjectsSettingsCapsForEditorPrePopulation()
    {
        var settings = new HonuaAdminServiceSettingsResponse
        {
            ServiceName = "svc",
            EnabledProtocols = ["FeatureServer"],
            SettingsCaps = new HonuaAdminServiceSettingsCapsResponse
            {
                MaxRecordCount = 3000,
                QueryTimeoutMs = 15000,
                SupportedFormats = ["json", "pbf"],
                DefaultFormat = "json",
                SupportsAttachments = true,
                MaxAttachmentSizeBytes = 1_048_576
            }
        };
        var operation = new HonuaServerServiceConfigurationOperation(CreateClient(_ => Ok(settings)));

        var view = await operation.GetSettingsAsync("svc");

        Assert.True(view.Bound);
        Assert.NotNull(view.SettingsCaps);
        Assert.Equal(3000, view.SettingsCaps!.MaxRecordCount);
        Assert.Equal(15000, view.SettingsCaps.QueryTimeoutMs);
        Assert.Equal(["json", "pbf"], view.SettingsCaps.SupportedFormats);
        Assert.Equal("json", view.SettingsCaps.DefaultFormat);
        Assert.True(view.SettingsCaps.SupportsAttachments);
        Assert.Equal(1_048_576, view.SettingsCaps.MaxAttachmentSizeBytes);
    }

    [Fact]
    public async Task GetSettingsCaps_ReadsDedicatedCapsEndpoint()
    {
        string? path = null;
        var caps = new HonuaAdminServiceSettingsCapsResponse { MaxRecordCount = 1234 };
        var client = CreateClient(request =>
        {
            path = request.RequestUri?.AbsolutePath;
            return Ok(caps);
        });

        var result = await client.GetServiceSettingsCapsAsync("svc");

        Assert.NotNull(result.Data);
        Assert.Equal(1234, result.Data!.MaxRecordCount);
        Assert.Equal("/api/v1/admin/services/svc/settings-caps", path);
    }

    [Fact]
    public async Task UpdateSettingsCaps_When501NotImplemented_SurfacesUnsupportedHonestly()
    {
        // The known gap: the server may answer 501 on the settings-caps write path. The operation must NOT
        // fabricate a success — it surfaces an honest "Unsupported" result with a caps-specific message.
        var operation = new HonuaServerServiceConfigurationOperation(CreateClient(_ =>
            new HttpResponseMessage(HttpStatusCode.NotImplemented)));

        var result = await operation.UpdateServiceSettingsCapsAsync(new ServiceSettingsCapsCommand
        {
            ServiceName = "svc",
            MaxRecordCount = 2000
        });

        Assert.False(result.Succeeded);
        Assert.Equal("Unsupported", result.State);
        Assert.Contains("Settings caps are not supported on this server", result.Detail!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UpdateSettingsCaps_WhenServerRejectsNegativeCap_DoesNotClaimSuccess()
    {
        // The server rejects negative caps (400). The operation surfaces the rejection verbatim, never a success.
        var operation = new HonuaServerServiceConfigurationOperation(CreateClient(_ =>
            new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = JsonContent.Create(new { success = false, message = "maxRecordCount must be non-negative." })
            }));

        var result = await operation.UpdateServiceSettingsCapsAsync(new ServiceSettingsCapsCommand
        {
            ServiceName = "svc",
            MaxRecordCount = -1
        });

        Assert.False(result.Succeeded);
        Assert.NotEqual("Updated", result.State);
        Assert.Contains("non-negative", result.Detail!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UpdateSettingsCaps_OnTransportError_DoesNotClaimSuccess()
    {
        var operation = new HonuaServerServiceConfigurationOperation(CreateClient(_ =>
            throw new HttpRequestException("connection refused")));

        var result = await operation.UpdateServiceSettingsCapsAsync(new ServiceSettingsCapsCommand
        {
            ServiceName = "svc",
            MaxRecordCount = 2000
        });

        Assert.False(result.Succeeded);
        Assert.False(string.IsNullOrWhiteSpace(result.Detail));
    }

    [Fact]
    public async Task Unsupported_SettingsCapsSave_NeverCallsNetwork_AndReturnsMissingBinding()
    {
        var operation = new UnsupportedServiceConfigurationOperation();

        var result = await operation.UpdateServiceSettingsCapsAsync(new ServiceSettingsCapsCommand
        {
            ServiceName = "svc",
            MaxRecordCount = 2000
        });

        Assert.False(result.Succeeded);
        Assert.Equal("Missing binding", result.State);
        Assert.Contains("HONUA_SERVER_BASE_URL", result.Detail!, StringComparison.Ordinal);
    }

    private static HttpResponseMessage Ok<T>(T data) =>
        new(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { success = true, data, timestamp = DateTimeOffset.UtcNow })
        };

    private static HonuaAdminOperateHttpClient CreateClient(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var httpClient = new HttpClient(new StubHandler(responder)) { BaseAddress = BaseAddress };
        return new HonuaAdminOperateHttpClient(httpClient, new HonuaAdminOperateClientOptions(BaseAddress, "test-key"));
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // Materialize content before returning so the request-body assertion can read it.
            _ = request.Content?.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult();
            return Task.FromResult(responder(request));
        }
    }
}
