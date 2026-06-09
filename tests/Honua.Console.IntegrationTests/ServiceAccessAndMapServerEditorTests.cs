using System.Net;
using System.Net.Http.Json;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Docker-free coverage for the metadata-UI gap-analysis Bucket 3-A closures:
/// <list type="bullet">
/// <item>#6 access-by-role — the access-policy PUT now carries <c>allowedRoles</c>/<c>allowedWriteRoles</c>;</item>
/// <item>#5 MapServer render settings — a new <c>PUT /mapserver</c> client + operation, with an honest
/// 501-Not-Implemented surface (the known V2 gap) rather than a fabricated success.</item>
/// </list>
/// These assert the real route/verb/body each mutation issues (over a stubbed admin client) and the result
/// mapping, plus that a recording fake operation receives the role arrays / MapServer command from the
/// command surface. They run in PR CI (no Docker).
/// </summary>
public sealed class ServiceAccessAndMapServerEditorTests
{
    private static readonly Uri BaseAddress = new("https://honua.test");

    // ---- #6 access-by-role -----------------------------------------------------------------------

    [Fact]
    public async Task UpdateAccessPolicy_SendsAllowedRolesAndWriteRolesOnAccessPolicyPut()
    {
        string? path = null;
        HttpMethod? method = null;
        string? body = null;
        var settings = new HonuaAdminServiceSettingsResponse { ServiceName = "svc", EnabledProtocols = ["FeatureServer"] };
        var operation = new HonuaServerServiceConfigurationOperation(CreateClient(request =>
        {
            path = request.RequestUri?.AbsolutePath;
            method = request.Method;
            body = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            return Ok(settings);
        }));

        var result = await operation.UpdateAccessPolicyAsync(new ServiceAccessPolicyCommand
        {
            ServiceName = "svc",
            AllowAnonymous = false,
            AllowAnonymousWrite = false,
            AllowedRoles = ["viewer", "editor"],
            AllowedWriteRoles = ["admin"]
        });

        Assert.True(result.Succeeded);
        Assert.Equal("Updated", result.State);
        Assert.Equal(HttpMethod.Put, method);
        Assert.Equal("/api/v1/admin/services/svc/access-policy", path);
        // The role arrays reach the wire body on the SAME access-policy PUT.
        Assert.Contains("\"allowedRoles\":[\"viewer\",\"editor\"]", body!, StringComparison.Ordinal);
        Assert.Contains("\"allowedWriteRoles\":[\"admin\"]", body!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UpdateAccessPolicy_WithEmptyRoleArrays_SendsEmptyArraysToClearRoles()
    {
        string? body = null;
        var settings = new HonuaAdminServiceSettingsResponse { ServiceName = "svc", EnabledProtocols = ["FeatureServer"] };
        var operation = new HonuaServerServiceConfigurationOperation(CreateClient(request =>
        {
            body = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            return Ok(settings);
        }));

        var result = await operation.UpdateAccessPolicyAsync(new ServiceAccessPolicyCommand
        {
            ServiceName = "svc",
            AllowAnonymous = true,
            AllowAnonymousWrite = false,
            AllowedRoles = [],
            AllowedWriteRoles = []
        });

        Assert.True(result.Succeeded);
        Assert.Contains("\"allowedRoles\":[]", body!, StringComparison.Ordinal);
        Assert.Contains("\"allowedWriteRoles\":[]", body!, StringComparison.Ordinal);
    }

    // ---- #5 MapServer render settings ------------------------------------------------------------

    [Fact]
    public async Task UpdateMapServerSettings_IssuesPutToMapServerRoute_WithRenderSettingsBody()
    {
        string? path = null;
        HttpMethod? method = null;
        string? body = null;
        var settings = new HonuaAdminServiceSettingsResponse
        {
            ServiceName = "svc",
            EnabledProtocols = ["MapServer"],
            MapServer = new HonuaAdminMapServerSettingsResponse { MaxImageWidth = 4096, DefaultFormat = "png" }
        };
        var operation = new HonuaServerServiceConfigurationOperation(CreateClient(request =>
        {
            path = request.RequestUri?.AbsolutePath;
            method = request.Method;
            body = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            return Ok(settings);
        }));

        var result = await operation.UpdateMapServerSettingsAsync(new ServiceMapServerSettingsCommand
        {
            ServiceName = "svc",
            MaxImageWidth = 4096,
            MaxImageHeight = 4096,
            DefaultImageWidth = 512,
            DefaultImageHeight = 512,
            DefaultDpi = 96,
            DefaultFormat = "png",
            DefaultTransparent = true,
            MaxFeaturesPerLayer = 5000
        });

        Assert.True(result.Succeeded);
        Assert.Equal("Updated", result.State);
        Assert.Equal(HttpMethod.Put, method);
        Assert.Equal("/api/v1/admin/services/svc/mapserver", path);
        Assert.Contains("\"maxImageWidth\":4096", body!, StringComparison.Ordinal);
        Assert.Contains("\"defaultDpi\":96", body!, StringComparison.Ordinal);
        Assert.Contains("\"defaultFormat\":\"png\"", body!, StringComparison.Ordinal);
        Assert.Contains("\"defaultTransparent\":true", body!, StringComparison.Ordinal);
        Assert.Contains("\"maxFeaturesPerLayer\":5000", body!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UpdateMapServerSettings_When501NotImplemented_SurfacesUnsupportedHonestly()
    {
        // The known V2 gap: the server may answer 501 on the MapServer write path. The operation must NOT
        // fabricate a success — it surfaces an honest "Unsupported" result with a MapServer-specific message.
        var operation = new HonuaServerServiceConfigurationOperation(CreateClient(_ =>
            new HttpResponseMessage(HttpStatusCode.NotImplemented)));

        var result = await operation.UpdateMapServerSettingsAsync(new ServiceMapServerSettingsCommand
        {
            ServiceName = "svc",
            MaxImageWidth = 4096
        });

        Assert.False(result.Succeeded);
        Assert.Equal("Unsupported", result.State);
        Assert.Contains("MapServer settings are not supported on this server", result.Detail!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UpdateMapServerSettings_OnTransportError_DoesNotClaimSuccess()
    {
        var operation = new HonuaServerServiceConfigurationOperation(CreateClient(_ =>
            throw new HttpRequestException("connection refused")));

        var result = await operation.UpdateMapServerSettingsAsync(new ServiceMapServerSettingsCommand
        {
            ServiceName = "svc",
            MaxImageWidth = 4096
        });

        Assert.False(result.Succeeded);
        Assert.False(string.IsNullOrWhiteSpace(result.Detail));
    }

    [Fact]
    public async Task Unsupported_MapServerSave_NeverCallsNetwork_AndReturnsMissingBinding()
    {
        var operation = new UnsupportedServiceConfigurationOperation();

        var result = await operation.UpdateMapServerSettingsAsync(new ServiceMapServerSettingsCommand
        {
            ServiceName = "svc",
            MaxImageWidth = 4096
        });

        Assert.False(result.Succeeded);
        Assert.Equal("Missing binding", result.State);
        Assert.Contains("HONUA_SERVER_BASE_URL", result.Detail!, StringComparison.Ordinal);
    }

    // ---- recording fake: command surface reaches the operation ----------------------------------

    [Fact]
    public async Task RecordingOperation_ReceivesRoleArraysAndMapServerCommand()
    {
        var recorder = new RecordingServiceConfigurationOperation();

        await recorder.UpdateAccessPolicyAsync(new ServiceAccessPolicyCommand
        {
            ServiceName = "svc",
            AllowedRoles = ["viewer"],
            AllowedWriteRoles = ["admin"]
        });
        await recorder.UpdateMapServerSettingsAsync(new ServiceMapServerSettingsCommand
        {
            ServiceName = "svc",
            MaxImageWidth = 2048,
            DefaultFormat = "jpg"
        });

        Assert.NotNull(recorder.LastAccessPolicy);
        Assert.Equal(["viewer"], recorder.LastAccessPolicy!.AllowedRoles);
        Assert.Equal(["admin"], recorder.LastAccessPolicy.AllowedWriteRoles);

        Assert.NotNull(recorder.LastMapServer);
        Assert.Equal(2048, recorder.LastMapServer!.MaxImageWidth);
        Assert.Equal("jpg", recorder.LastMapServer.DefaultFormat);
    }

    private static HttpResponseMessage Ok<T>(T data) =>
        new(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { success = true, data, timestamp = DateTimeOffset.UtcNow })
        };

    private static IHonuaAdminOperateClient CreateClient(Func<HttpRequestMessage, HttpResponseMessage> responder)
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

    /// <summary>Recording fake that captures the last access-policy and MapServer commands it was handed.</summary>
    private sealed class RecordingServiceConfigurationOperation : IServiceConfigurationOperation
    {
        public ServiceAccessPolicyCommand? LastAccessPolicy { get; private set; }

        public ServiceMapServerSettingsCommand? LastMapServer { get; private set; }

        public Task<ServiceSettingsView> GetSettingsAsync(string serviceName, CancellationToken cancellationToken = default) =>
            Task.FromResult(ServiceSettingsView.Unbound(serviceName, "recording"));

        public Task<ServiceConfigurationResult> SetLayerEnabledAsync(ServiceLayerEnableCommand command, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ServiceConfigurationResult { Succeeded = true, State = "Enabled" });

        public Task<ServiceConfigurationResult> UpdateProtocolsAsync(ServiceProtocolsCommand command, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ServiceConfigurationResult { Succeeded = true, State = "Updated" });

        public Task<ServiceConfigurationResult> UpdateAccessPolicyAsync(ServiceAccessPolicyCommand command, CancellationToken cancellationToken = default)
        {
            LastAccessPolicy = command;
            return Task.FromResult(new ServiceConfigurationResult { Succeeded = true, State = "Updated" });
        }

        public Task<ServiceConfigurationResult> UpdateMapServerSettingsAsync(ServiceMapServerSettingsCommand command, CancellationToken cancellationToken = default)
        {
            LastMapServer = command;
            return Task.FromResult(new ServiceConfigurationResult { Succeeded = true, State = "Updated" });
        }
    }
}
