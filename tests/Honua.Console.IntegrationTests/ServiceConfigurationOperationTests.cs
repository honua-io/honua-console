using System.Net;
using System.Net.Http.Json;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Docker-free coverage for the Wave 5 service-configuration OPERATIONS: the
/// <see cref="HonuaServerServiceConfigurationOperation"/> over a stubbed admin client and the
/// missing-binding <see cref="UnsupportedServiceConfigurationOperation"/>. Asserts the real route/verb/body
/// each mutation issues and the result mapping (Enabled/Disabled/Updated + post-change state), plus that the
/// unconfigured surface never performs a network call. These run in PR CI (no Docker); the live round-trips
/// in <see cref="ServiceConfigurationRoundTripTests"/> cover the server state independently.
/// </summary>
public sealed class ServiceConfigurationOperationTests
{
    private static readonly Uri BaseAddress = new("https://honua.test");

    [Fact]
    public async Task SetLayerEnabled_Disable_IssuesPutWithEnabledBody_AndMapsDisabled()
    {
        string? path = null;
        HttpMethod? method = null;
        string? body = null;
        var summary = new HonuaAdminPublishedLayerSummary { LayerId = 7, ServiceName = "svc", Enabled = false };
        var operation = new HonuaServerServiceConfigurationOperation(CreateClient(request =>
        {
            path = request.RequestUri?.AbsolutePath;
            method = request.Method;
            body = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            return Ok(summary);
        }));

        var result = await operation.SetLayerEnabledAsync(new ServiceLayerEnableCommand
        {
            ConnectionId = "conn-1",
            LayerId = 7,
            ServiceName = "svc",
            Enabled = false
        });

        Assert.True(result.Succeeded);
        Assert.Equal("Disabled", result.State);
        Assert.False(result.Enabled ?? true);
        Assert.Equal(HttpMethod.Put, method);
        Assert.Equal("/api/v1/admin/connections/conn-1/layers/7/enabled", path);
        Assert.Contains("\"enabled\":false", body!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SetLayerEnabled_Enable_MapsEnabled()
    {
        var summary = new HonuaAdminPublishedLayerSummary { LayerId = 7, ServiceName = "svc", Enabled = true };
        var operation = new HonuaServerServiceConfigurationOperation(CreateClient(_ => Ok(summary)));

        var result = await operation.SetLayerEnabledAsync(new ServiceLayerEnableCommand
        {
            ConnectionId = "conn-1",
            LayerId = 7,
            Enabled = true
        });

        Assert.True(result.Succeeded);
        Assert.Equal("Enabled", result.State);
        Assert.True(result.Enabled ?? false);
    }

    [Fact]
    public async Task UpdateProtocols_IssuesPutToProtocolsRoute_AndReturnsServerProtocols()
    {
        string? path = null;
        string? body = null;
        var settings = new HonuaAdminServiceSettingsResponse
        {
            ServiceName = "svc",
            EnabledProtocols = ["FeatureServer", "MapServer"]
        };
        var operation = new HonuaServerServiceConfigurationOperation(CreateClient(request =>
        {
            path = request.RequestUri?.AbsolutePath;
            body = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            return Ok(settings);
        }));

        var result = await operation.UpdateProtocolsAsync(new ServiceProtocolsCommand
        {
            ServiceName = "svc",
            EnabledProtocols = ["FeatureServer", "MapServer"]
        });

        Assert.True(result.Succeeded);
        Assert.Equal("Updated", result.State);
        Assert.Equal("/api/v1/admin/services/svc/protocols", path);
        Assert.Contains("\"enabledProtocols\":[\"FeatureServer\",\"MapServer\"]", body!, StringComparison.Ordinal);
        Assert.Equal(["FeatureServer", "MapServer"], result.EnabledProtocols);
    }

    [Fact]
    public async Task UpdateProtocols_WhenServerRejects_MapsFailureWithDetail()
    {
        var operation = new HonuaServerServiceConfigurationOperation(CreateClient(_ =>
            new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = JsonContent.Create(new { success = false, message = "Invalid protocol(s): Nope." })
            }));

        var result = await operation.UpdateProtocolsAsync(new ServiceProtocolsCommand
        {
            ServiceName = "svc",
            EnabledProtocols = ["Nope"]
        });

        Assert.False(result.Succeeded);
        Assert.Contains("Invalid protocol", result.Detail!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UpdateProtocols_WhenServerReturnsFieldErrors_PropagatesThemToResult()
    {
        var operation = new HonuaServerServiceConfigurationOperation(CreateClient(_ =>
            new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = JsonContent.Create(new
                {
                    title = "Validation failed",
                    errors = new[]
                    {
                        new { code = "invalid_protocol", severity = "error", path = "enabledProtocols[1]", fieldId = "enabledProtocols", message = "'Nope' is not a valid protocol." }
                    }
                })
            }));

        var result = await operation.UpdateProtocolsAsync(new ServiceProtocolsCommand
        {
            ServiceName = "svc",
            EnabledProtocols = ["FeatureServer", "Nope"]
        });

        Assert.False(result.Succeeded);
        var error = Assert.Single(result.FieldErrors);
        Assert.Equal("invalid_protocol", error.Code);
        Assert.Equal("enabledProtocols", error.FieldId);
        Assert.Contains("not a valid protocol", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UpdateAccessPolicy_IssuesPutToAccessPolicyRoute_WithPolicyBody()
    {
        string? path = null;
        string? body = null;
        var settings = new HonuaAdminServiceSettingsResponse { ServiceName = "svc", EnabledProtocols = ["FeatureServer"] };
        var operation = new HonuaServerServiceConfigurationOperation(CreateClient(request =>
        {
            path = request.RequestUri?.AbsolutePath;
            body = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            return Ok(settings);
        }));

        var result = await operation.UpdateAccessPolicyAsync(new ServiceAccessPolicyCommand
        {
            ServiceName = "svc",
            AllowAnonymous = true,
            AllowAnonymousWrite = false,
            AllowedRoles = ["viewer"]
        });

        Assert.True(result.Succeeded);
        Assert.Equal("Updated", result.State);
        Assert.Equal("/api/v1/admin/services/svc/access-policy", path);
        Assert.Contains("\"allowAnonymous\":true", body!, StringComparison.Ordinal);
        Assert.Contains("\"allowedRoles\":[\"viewer\"]", body!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unsupported_NeverCallsNetwork_AndReturnsMissingBinding()
    {
        var operation = new UnsupportedServiceConfigurationOperation();

        var enable = await operation.SetLayerEnabledAsync(new ServiceLayerEnableCommand
        {
            ConnectionId = "conn-1",
            LayerId = 1,
            Enabled = true
        });
        var protocols = await operation.UpdateProtocolsAsync(new ServiceProtocolsCommand
        {
            ServiceName = "svc",
            EnabledProtocols = ["FeatureServer"]
        });
        var policy = await operation.UpdateAccessPolicyAsync(new ServiceAccessPolicyCommand { ServiceName = "svc" });

        foreach (var result in new[] { enable, protocols, policy })
        {
            Assert.False(result.Succeeded);
            Assert.Equal("Missing binding", result.State);
            Assert.Contains("HONUA_SERVER_BASE_URL", result.Detail!, StringComparison.Ordinal);
        }
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
}
