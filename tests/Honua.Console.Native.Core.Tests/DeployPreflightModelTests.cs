using Honua.Console.Contracts;
using Honua.Console.Shell.Models;

namespace Honua.Console.Native.Core.Tests;

public sealed class DeployPreflightModelTests
{
    [Fact]
    public void Map_ObservedServingRevision_UsesLiveReadbackAndDoesNotSubstituteConfiguredImage()
    {
        const string json = """
            {"status":"ready","readyForCoordinatedDeploy":true,"message":"ready",
             "platformRelease":{"releaseDeclared":true,"isCoVersioned":false,
             "serving":[{"id":"compose-local","effectiveArtifactReference":"configured-image",
             "observationStatus":"observed","observedImageId":"sha256:live-image",
             "serverVersion":"2026.1-live"},
             {"id":"unavailable","effectiveArtifactReference":"configured-only","observationStatus":"unavailable"}]}}
            """;
        var response = System.Text.Json.JsonSerializer.Deserialize<DeployPreflightResponse>(json)!;
        var view = DeployPreflightMapper.Map(response);
        Assert.Equal("sha256:live-image", view.Serving[0].ObservedImageId);
        Assert.Equal("2026.1-live", view.Serving[0].ServerVersion);
        Assert.Equal("observed", view.Serving[0].ObservationStatus);
        Assert.Null(view.Serving[1].ObservedImageId);
        Assert.Null(view.Serving[1].ServerVersion);
        Assert.Equal("unavailable", view.Serving[1].ObservationStatus);
    }

    [Fact]
    public void Map_ReadyWithNoDiagnostics_BlocksSubmitIsFalseAndDiagnosticsNotIncluded()
    {
        var response = new DeployPreflightResponse
        {
            Status = "ready",
            ReadyForCoordinatedDeploy = true,
            Message = "Instance is ready for coordinated deployment.",
        };

        var view = DeployPreflightMapper.Map(response);

        Assert.False(view.BlocksSubmit);
        Assert.False(view.DiagnosticsIncluded);
        Assert.Empty(view.BlockingReasons);
    }

    [Fact]
    public void Map_NotReadyWithPendingContractScripts_ReportsBlockingReasonNamingScriptCount()
    {
        var response = new DeployPreflightResponse
        {
            Status = "blocked",
            ReadyForCoordinatedDeploy = false,
            Message = "Instance is not ready for coordinated deployment.",
            Migration = new DeployPreflightMigrationResponse
            {
                LifecycleStatus = "pending",
                PendingScripts = ["020_add_column.sql", "021_contract_change.sql"],
                UpgradeRequired = true,
                PlanAvailable = true,
            },
        };

        var view = DeployPreflightMapper.Map(response);

        Assert.True(view.BlocksSubmit);
        Assert.True(view.DiagnosticsIncluded);
        Assert.Equal(2, view.PendingScripts.Count);
        Assert.Contains(view.BlockingReasons, r => r.Contains("2 pending contract migration script"));
    }

    [Fact]
    public void Map_NotReadyDueToSkewedPlatformRelease_ReportsSkewedPlaneIds()
    {
        var response = new DeployPreflightResponse
        {
            Status = "blocked",
            ReadyForCoordinatedDeploy = false,
            Message = "not ready",
            PlatformRelease = new DeployPreflightPlatformReleaseResponse
            {
                ReleaseDeclared = true,
                IsCoVersioned = false,
                SkewedIds = ["worker-2"],
            },
        };

        var view = DeployPreflightMapper.Map(response);

        Assert.True(view.BlocksSubmit);
        Assert.True(view.PlatformReleaseDeclared);
        Assert.False(view.PlatformReleaseCoVersioned);
        Assert.Contains(view.BlockingReasons, r => r.Contains("worker-2"));
    }

    [Fact]
    public void Map_AbsentDatabaseCompatibility_DefaultsToCompatibleRatherThanBlocking()
    {
        // Null-omission: an absent databaseCompatibility block must not be misread as
        // "incompatible" — the server only sends the block when includeDiagnostics=true.
        var response = new DeployPreflightResponse
        {
            Status = "ready",
            ReadyForCoordinatedDeploy = true,
            Message = "ready",
        };

        var view = DeployPreflightMapper.Map(response);

        Assert.True(view.DatabaseCompatible);
    }
}
