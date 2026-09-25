using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Pins the governed Enterprise contract independently of the release-posture authoring lane.
/// A pending operation is not a created draft and must never be treated as synchronous success.
/// </summary>
public sealed class StudioApprovalContractIntegrationTests
{
    [SkippableFact]
    public async Task EnterpriseCreateDraft_RequiresApproval_AndDoesNotCreateDraft()
    {
        var options = ConsoleTrustIntegrationOptions.Load();
        Skip.If(ConsoleTrustIntegrationOptions.GetStudioSkipReason() is not null,
            ConsoleTrustIntegrationOptions.GetStudioSkipReason());
        Skip.If(string.IsNullOrWhiteSpace(options.ServerImage),
            "Set HONUA_CONSOLE_SERVER_IMAGE for the isolated Enterprise approval contract.");

        options = options with
        {
            ExternalBaseUri = null,
            ServerScheme = "http",
            ServerEnvironment = string.Join('\n', options.ServerEnvironment,
                "ASPNETCORE_ENVIRONMENT=Test",
                "HONUA_DEV_AUTH=false",
                "HONUA_DEV_AUTH_ALLOW_BYPASS=false",
                "Licensing__Mode=Enabled",
                "Licensing__DevGrantEdition=Enterprise")
        };

        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        await using var server = await HonuaServerTestcontainer.StartAsync(options, timeout.Token);
        using var client = new HttpClient { BaseAddress = server.BaseAddress };
        client.DefaultRequestHeaders.Add("X-API-Key", options.StudioAdminApiKey);
        var packageKey = "console-approval-contract-" + Guid.NewGuid().ToString("N");
        using var response = await client.PostAsJsonAsync("/api/v1/studio/package-drafts", new
        {
            packageKey,
            envelope = new { family = "map", schemaVersion = "1.0" }
        }, timeout.Token);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        using var envelope = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
        Assert.True(envelope.RootElement.GetProperty("success").GetBoolean());
        var operation = envelope.RootElement.GetProperty("data");
        Assert.Equal("studio.draft.create", operation.GetProperty("operationId").GetString());
        Assert.Equal(4, operation.GetProperty("status").GetInt32()); // OperationHandleStatus.RequiresApproval
        Assert.Equal("control-plane", operation.GetProperty("approvalLane").GetString());
        Assert.False(string.IsNullOrWhiteSpace(operation.GetProperty("proposalId").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(operation.GetProperty("operationInstanceId").GetString()));
        Assert.False(operation.TryGetProperty("draftId", out _));

        // Read independently: approval acceptance must not have run the draft actuator.
        using var read = await client.GetAsync("/api/v1/studio/package-drafts?q=" + packageKey, timeout.Token);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        using var drafts = JsonDocument.Parse(await read.Content.ReadAsStringAsync(timeout.Token));
        Assert.True(drafts.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(0, drafts.RootElement.GetProperty("data").GetProperty("total").GetInt32());
        Assert.Empty(drafts.RootElement.GetProperty("data").GetProperty("items").EnumerateArray());
    }
}
