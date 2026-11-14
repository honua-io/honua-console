using System.Globalization;
using System.Text.Json;
using Bunit;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

// The Operate temporal page with the Console's real server client against a live honua-server.
// Layer publish cannot set a temporal column yet, so a live layer reports no history; the history branch
// asserts whatever the server declares and never expects a fabricated checkpoint.
[Collection(TemporalReplicaIntegrationCollection.Name)]
public sealed class OperateTemporalLiveServerTests
{
    private readonly TemporalReplicaFixture _fixture;

    public OperateTemporalLiveServerTests(TemporalReplicaFixture fixture)
    {
        _fixture = fixture;
    }

    [SkippableFact]
    public async Task TemporalViewer_RendersTemporalHistory_FromLiveServer()
    {
        Skip.If(_fixture.SkipReason is not null, _fixture.SkipReason ?? string.Empty);

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var seeded = await PublishedLayerSeeder.SeedPublishedLayerAsync(_fixture, suffix);
        var sourceKey = $"{seeded.ServiceName}/{seeded.LayerId.ToString(CultureInfo.InvariantCulture)}";

        using var verifier = _fixture.CreateVerifier();
        var serverCapability = await verifier.GetTemporalCapabilityAsync(seeded.ServiceName, seeded.LayerId);
        Assert.True(serverCapability is not null, "The live server did not serve the temporal capability route.");

        var client = CreateClient(seeded);
        using var ctx = CreateContext(client);
        var page = ctx.Render<OperateTemporalPage>();

        page.WaitForAssertion(
            () => Assert.Contains(sourceKey, page.Markup, StringComparison.Ordinal),
            TimeSpan.FromSeconds(10));
        Assert.DoesNotContain("Temporal capability is not bound", page.Markup, StringComparison.Ordinal);

        var source = Assert.Single((await client.GetWorkspaceAsync()).Sources);
        var checkpoints = await client.GetCheckpointsAsync(sourceKey);
        if (serverCapability!.SupportsHistory || serverCapability.SupportsAsOf)
        {
            Assert.NotEqual(TemporalMode.None, source.Mode);
            Assert.Null(checkpoints.BindingState);
            var checkpoint = Assert.Single(checkpoints.Checkpoints);
            Assert.Equal(
                serverCapability.CurrentGeneration?.ToString(CultureInfo.InvariantCulture),
                checkpoint.CursorValue);

            page.FindAll("button").First(b => b.TextContent.Trim() == "Inspect").Click();
            page.WaitForAssertion(
                () => Assert.Contains(
                    $"aria-label=\"{checkpoint.CursorType} {checkpoint.Label}\"",
                    page.Find("[data-temporal-region='timeline']").InnerHtml,
                    StringComparison.Ordinal),
                TimeSpan.FromSeconds(10));
        }
        else
        {
            Assert.Equal(TemporalMode.None, source.Mode);
            Assert.Empty(checkpoints.Checkpoints);
            Assert.NotNull(checkpoints.BindingState);
            page.FindAll("button").First(b => b.TextContent.Trim() == "Inspect").Click();
            page.WaitForAssertion(
                () => Assert.Contains("This source has no temporal history", page.Markup, StringComparison.Ordinal),
                TimeSpan.FromSeconds(10));
        }
    }

    [SkippableFact]
    public async Task TemporalViewer_RendersReplicaConflictReview_FromLiveServer()
    {
        Skip.If(_fixture.SkipReason is not null, _fixture.SkipReason ?? string.Empty);

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var seeded = await PublishedLayerSeeder.SeedPublishedLayerAsync(_fixture, suffix);
        var replicaName = $"console-review-{suffix}";
        var created = await PublishedLayerSeeder.CreateReplicaAsync(_fixture, seeded.ServiceName, replicaName);
        Skip.If(
            created.Skip,
            "The pinned honua-server image has no GeoServices createReplica verb or offline-sync entitlement.");
        Assert.True(created.Failure is null, created.Failure);
        var replicaId = created.ReplicaId!;

        // Independent server read first: the pending conflict ids for the new replica.
        var serverConflictIds = await ListPendingConflictIdsAsync(seeded.ServiceName, replicaId);

        var client = CreateClient(seeded);
        var review = await client.GetReplicaConflictReviewAsync(replicaId);

        Assert.Null(review.BindingState);
        Assert.NotNull(review.Replica);
        Assert.Equal(replicaId, review.Replica!.ReplicaId);
        Assert.Equal(replicaName, review.Replica.ReplicaName);
        Assert.Equal(serverConflictIds.Count, review.Replica.PendingConflictCount);
        Assert.Equal(
            serverConflictIds.Order(StringComparer.Ordinal),
            review.Conflicts.Select(c => c.ConflictId).Order(StringComparer.Ordinal));

        // A replica the configured sources do not own is reported as not configured, never coerced.
        var foreign = await client.GetReplicaConflictReviewAsync($"missing-{Guid.NewGuid():N}");
        Assert.Null(foreign.Replica);
        Assert.Empty(foreign.Conflicts);
        Assert.NotNull(foreign.BindingState);
    }

    [Fact]
    public async Task UnboundServer_TemporalClient_NeverFabricatesData()
    {
        var client = new UnsupportedTemporalCapabilityClient();

        var workspace = await client.GetWorkspaceAsync();
        var checkpoints = await client.GetCheckpointsAsync("src");
        var diff = await client.GetDiffAsync("src", "a", "b");
        var queue = await client.GetReplicaConflictQueueAsync("src");
        var resolution = await client.ResolveConflictsAsync(
            new SyncConflictResolutionRequest(["c-1"], SyncResolutionAction.KeepServer));

        Assert.Empty(workspace.Sources);
        Assert.Empty(checkpoints.Checkpoints);
        Assert.True(checkpoints.BindingState!.IsMissingBinding);
        Assert.True(diff.BindingState!.IsMissingBinding);
        Assert.Empty(diff.SampleFeatureChanges);
        Assert.Empty(queue.Replicas);
        Assert.True(queue.BindingState!.IsMissingBinding);
        Assert.False(resolution.WroteAuditedChangeSet);
    }

    private HonuaServerTemporalCapabilityClient CreateClient(PublishedLayerSeeder.SeededLayer seeded) =>
        new(
            _fixture.CreateTemporalClient(),
            new HonuaServerTemporalOptions([new TemporalSourceCandidate(seeded.ServiceName, seeded.LayerId)]));

    private static BunitContext CreateContext(ITemporalCapabilityClient client)
    {
        var ctx = new BunitContext();
        ctx.AddConsoleNotifications();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddSingleton(ConsoleCapabilityTestManifest.All);
        ctx.Services.AddSingleton(client);
        return ctx;
    }

    private async Task<IReadOnlyList<string>> ListPendingConflictIdsAsync(string serviceName, string replicaId)
    {
        using var http = _fixture.CreateRawClient();
        http.DefaultRequestHeaders.TryAddWithoutValidation("X-API-Key", _fixture.AdminApiKey);
        using var response = await http.GetAsync(
            $"/api/v1/admin/services/{Uri.EscapeDataString(serviceName)}/replicas/{Uri.EscapeDataString(replicaId)}/conflicts?status=pending");
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"conflict list returned {(int)response.StatusCode}: {body}");

        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object)
        {
            root = data;
        }

        Assert.True(
            root.TryGetProperty("conflicts", out var conflicts) && conflicts.ValueKind == JsonValueKind.Array,
            $"conflict list has no conflicts array: {body}");

        return conflicts.EnumerateArray()
            .Select(c => c.GetProperty("conflictId").GetString()!)
            .ToList();
    }
}
