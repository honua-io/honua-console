using Honua.Console.Shell.Models;

namespace Honua.Console.Native.Core.Tests;

/// <summary>
/// Pure (no-I/O) coverage of the server-upgrade version-mismatch gate. The gate is the
/// safety core of the server-upgrade flow: an upgrade may proceed ONLY when the target
/// environment is strictly behind the requested version. Already-current (no-op), ahead
/// (downgrade), and unparseable cases are all gated. These cases pin that behavior so a
/// regression cannot silently submit a no-op or a downgrade as an "upgrade".
/// </summary>
public sealed class ServerUpgradePlanTests
{
    private static ServerVersionInfo Version(string version) => new(
        ServerVersion: version,
        ApiVersion: "v1",
        MetadataApiVersion: "v2",
        MetadataSchemaVersion: "2",
        DeploymentEnvironment: "staging",
        ObservedAt: DateTimeOffset.UtcNow);

    [Fact]
    public void Behind_AllowsProceed_WithNoGateReason()
    {
        var plan = ServerUpgradePlan.Create(Version("1.4.2"), "1.5.0");

        Assert.Equal(ServerUpgradeMismatch.Behind, plan.Mismatch);
        Assert.True(plan.CanProceed);
        Assert.Equal(string.Empty, plan.GateReason);
    }

    [Fact]
    public void Current_GatesProceed_AsNoOp()
    {
        var plan = ServerUpgradePlan.Create(Version("1.5.0"), "1.5.0");

        Assert.Equal(ServerUpgradeMismatch.Current, plan.Mismatch);
        Assert.False(plan.CanProceed);
        Assert.Contains("already runs", plan.GateReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Ahead_GatesProceed_AsDowngrade()
    {
        var plan = ServerUpgradePlan.Create(Version("1.6.0"), "1.5.0");

        Assert.Equal(ServerUpgradeMismatch.Ahead, plan.Mismatch);
        Assert.False(plan.CanProceed);
        Assert.Contains("downgrade", plan.GateReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OlderTarget_IsDetectedAsBehind_AndUpgradable()
    {
        // The explicit acceptance case: the target env is on an OLDER Honua version.
        var plan = ServerUpgradePlan.Create(Version("1.2.9"), "2.0.0");

        Assert.Equal(ServerUpgradeMismatch.Behind, plan.Mismatch);
        Assert.True(plan.CanProceed);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-version")]
    public void UnparseableRequested_IsUnknown_AndGated(string? requested)
    {
        var plan = ServerUpgradePlan.Create(Version("1.4.2"), requested);

        Assert.Equal(ServerUpgradeMismatch.Unknown, plan.Mismatch);
        Assert.False(plan.CanProceed);
        Assert.Contains("could not be parsed", plan.GateReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UnparseableCurrent_IsUnknown_AndGated()
    {
        var plan = ServerUpgradePlan.Create(Version("dev-snapshot"), "1.5.0");

        Assert.Equal(ServerUpgradeMismatch.Unknown, plan.Mismatch);
        Assert.False(plan.CanProceed);
    }

    [Fact]
    public void NullCurrent_IsUnknown_AndGated()
    {
        var plan = ServerUpgradePlan.Create(null, "1.5.0");

        Assert.Equal(ServerUpgradeMismatch.Unknown, plan.Mismatch);
        Assert.False(plan.CanProceed);
    }

    [Theory]
    [InlineData("1.5.0+build.42", "1.5.0")]      // build metadata stripped -> equal -> current
    [InlineData("v1.4.0", "1.5.0")]               // leading v + behind
    [InlineData("1.5.0-rc.1", "1.5.0")]           // pre-release core equals release -> current
    public void VersionSuffixes_AreNormalized(string current, string requested)
    {
        var plan = ServerUpgradePlan.Create(Version(current), requested);

        Assert.NotEqual(ServerUpgradeMismatch.Unknown, plan.Mismatch);
    }

    [Fact]
    public void SemanticVersion_BareMajor_PadsToMajorMinor()
    {
        Assert.Equal(new Version(2, 0), SemanticVersion.TryParse("2"));
    }

    [Fact]
    public void SemanticVersion_Garbage_IsNull()
    {
        Assert.Null(SemanticVersion.TryParse("garbage"));
        Assert.Null(SemanticVersion.TryParse(null));
    }
}
