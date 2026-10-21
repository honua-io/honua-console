using System.Net;
using System.Net.Http.Json;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Docker-free coverage for service settings caps. The dedicated caps routes are not registered on
/// the pinned server, so those calls return Unsupported and issue no HTTP. Caps carried on the
/// registered settings payload still pre-populate the editor. The missing-binding operation still
/// names HONUA_SERVER_BASE_URL.
/// </summary>
public sealed class ServiceSettingsCapsEditorTests
{
    private static readonly Uri BaseAddress = new("https://honua.test");

    [Fact]
    public async Task UnregisteredSettingsCapsRoutes_FailClosedWithoutHttp()
    {
        var calls = 0;
        var operation = new HonuaServerServiceConfigurationOperation(CreateClient(_ =>
        {
            calls++;
            throw new InvalidOperationException("unregistered route must not send HTTP");
        }));
        var client = CreateClient(_ =>
        {
            calls++;
            throw new InvalidOperationException("unregistered route must not send HTTP");
        });

        var write = await operation.UpdateServiceSettingsCapsAsync(new ServiceSettingsCapsCommand
        {
            ServiceName = "svc",
            MaxRecordCount = 2000
        });
        var read = await client.GetServiceSettingsCapsAsync("svc");

        Assert.Equal(0, calls);
        Assert.False(write.Succeeded);
        Assert.Equal(UnregisteredServerRoutes.State, write.State);
        Assert.Contains(UnregisteredServerRoutes.Detail, write.Detail, StringComparison.Ordinal);
        Assert.Equal(UnregisteredServerRoutes.State, read.Issue!.State);
        Assert.Equal(UnregisteredServerRoutes.StatusCode, read.Issue.StatusCode);
        Assert.Equal(UnregisteredServerRoutes.Detail, read.Issue.Detail);
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
