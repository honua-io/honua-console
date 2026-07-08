using System.Text.Json;
using Honua.Console.Contracts;

namespace Honua.Console.Native.Core.Tests;

/// <summary>
/// Console#290 addendum item 3: the pinned <c>DeployOperationResponse</c> / <c>DeployOperationListResponse</c>
/// wire contract (honua-server PR #2577) OMITS null-valued properties
/// (<c>DefaultIgnoreCondition = WhenWritingNull</c>). This test deserializes JSON shaped exactly like the
/// server's pinned example — including keys that are entirely absent, not present-as-null — through the real
/// source-generated Console DTOs, and asserts every absent optional property deserializes to
/// <see langword="null"/> (never a zero-value default such as <c>""</c> or <c>0</c>). #291's route-level
/// parity check does not catch this class of drift; only a shape test that actually deserializes does.
/// </summary>
public sealed class DeployOperationContractShapeTests
{
    // Shaped exactly like PR #2577's pinned example, with every nullable field OMITTED (not
    // present as `null`) except for one item that supplies the equally-real target-carrying
    // (server-upgrade / Deploy-kind) shape, since the pinned example only showed the
    // metadataRelease-carrying shape. Both are real shapes the same endpoint returns.
    private const string PinnedListJson = """
    {
      "items": [
        {
          "operationId": "op-manual-intervention-1",
          "kind": "Deploy",
          "status": "ManualInterventionRequired",
          "priority": "Normal",
          "target": {
            "targetId": "prod-serving-1",
            "targetKind": "Serving",
            "backend": "kubernetes",
            "environment": "prod",
            "targetName": "prod-serving",
            "desiredRevision": "1.6.0",
            "parameters": {}
          },
          "warnings": [],
          "blockingReasons": [],
          "createdAt": "2026-07-07T09:00:00+00:00",
          "updatedAt": "2026-07-07T09:05:00+00:00"
        },
        {
          "operationId": "op-metadata-promote-1",
          "kind": "MetadataRelease",
          "status": "Submitted",
          "priority": "Normal",
          "metadataRelease": {
            "packageId": "pkg-1",
            "desiredRevision": "rev-77",
            "targetEnvironment": "prod",
            "jobIds": [],
            "evidenceRefs": [],
            "currentStage": "Applying",
            "blockers": [],
            "warnings": []
          },
          "warnings": [],
          "blockingReasons": [],
          "createdAt": "2026-07-07T09:00:00+00:00",
          "updatedAt": "2026-07-07T09:05:00+00:00"
        }
      ],
      "page": 1,
      "pageSize": 50,
      "totalCount": 2,
      "hasMore": false
    }
    """;

    [Fact]
    public void DeployOperationListResponse_OmittedOptionalProperties_DeserializeToNull()
    {
        var response = JsonSerializer.Deserialize(
            PinnedListJson,
            DeployControlJsonContext.Default.DeployOperationListResponse);

        Assert.NotNull(response);
        Assert.Equal(2, response!.Items.Count);

        var manualIntervention = response.Items[0];
        // Absent, not present-as-null: providerOperationId / currentPhase / observedState /
        // errorMessage / requestedBy / reason / correlationId / metadataRelease / completedAt.
        Assert.Null(manualIntervention.ProviderOperationId);
        Assert.Null(manualIntervention.CurrentPhase);
        Assert.Null(manualIntervention.ObservedState);
        Assert.Null(manualIntervention.ErrorMessage);
        Assert.Null(manualIntervention.RequestedBy);
        Assert.Null(manualIntervention.Reason);
        Assert.Null(manualIntervention.CorrelationId);
        Assert.Null(manualIntervention.MetadataRelease);
        Assert.Null(manualIntervention.CompletedAt);

        // The target object IS present here and its own optional properties are absent too.
        Assert.NotNull(manualIntervention.Target);
        Assert.Null(manualIntervention.Target!.ArtifactReference);
        Assert.Null(manualIntervention.Target.RuntimeProfile);
        Assert.Null(manualIntervention.Target.CurrentRevision);
        // Non-null, non-omittable fields must still be exactly what was sent.
        Assert.Equal("1.6.0", manualIntervention.Target.DesiredRevision);
        Assert.Equal("prod", manualIntervention.Target.Environment);

        // Always-present-but-possibly-empty arrays deserialize as empty, never null.
        Assert.NotNull(manualIntervention.Warnings);
        Assert.Empty(manualIntervention.Warnings);
        Assert.NotNull(manualIntervention.BlockingReasons);
        Assert.Empty(manualIntervention.BlockingReasons);

        var metadataPromotion = response.Items[1];
        // The complementary shape: metadataRelease present, target absent.
        Assert.Null(metadataPromotion.Target);
        Assert.NotNull(metadataPromotion.MetadataRelease);
        Assert.Equal("rev-77", metadataPromotion.MetadataRelease!.DesiredRevision);
        // Nested optional fields on metadataRelease are also absent, not null-valued.
        Assert.Null(metadataPromotion.MetadataRelease.GitOperationId);
        Assert.Null(metadataPromotion.MetadataRelease.PrUrl);
        Assert.Null(metadataPromotion.MetadataRelease.CommitSha);
        Assert.Null(metadataPromotion.MetadataRelease.DeployOperationId);
        Assert.Null(metadataPromotion.MetadataRelease.RollbackPlan);
    }

    [Fact]
    public void DeployPreflightResponse_WithoutDiagnostics_LeavesDiagnosticFieldsNullNotDefaulted()
    {
        // Shaped like a call WITHOUT includeDiagnostics=true: only the always-present fields
        // are sent, every diagnostic block is entirely absent.
        const string json = """
        {
          "status": "ready",
          "readyForCoordinatedDeploy": true,
          "message": "Instance is ready for coordinated deployment.",
          "generatedAt": "2026-07-07T09:00:00+00:00"
        }
        """;

        var response = JsonSerializer.Deserialize(json, DeployControlJsonContext.Default.DeployPreflightResponse);

        Assert.NotNull(response);
        Assert.True(response!.ReadyForCoordinatedDeploy);
        Assert.Null(response.ServerVersion);
        Assert.Null(response.Environment);
        Assert.Null(response.DeploymentMode);
        Assert.Null(response.InstanceName);
        Assert.Null(response.Readiness);
        Assert.Null(response.Migration);
        Assert.Null(response.DatabaseCompatibility);
        Assert.Null(response.PlatformRelease);
    }
}
