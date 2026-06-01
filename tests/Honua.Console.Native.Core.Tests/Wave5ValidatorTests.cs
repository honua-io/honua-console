using Honua.Console.Shell.Models;
using Honua.Console.Shell.Validation;

namespace Honua.Console.Native.Core.Tests;

/// <summary>
/// Docker-free unit coverage for the Wave-5 non-Studio client validators: RBAC scoped-invite, Share manage,
/// Operate temporal diff selection, Operate publishing lookup, and the native environment-profile create form.
/// Each rule from the catalog is proven in its pass and fail state, keyed by the surface's field keys.
/// </summary>
public sealed class Wave5ValidatorTests
{
    // --- RBAC scoped-invite ---

    private static RbacInviteState ValidInvite() => new("name@example.gov", ScopeDev: true, ScopeStaging: false, ScopeProd: false, IpAllowlist: null);

    [Fact]
    public void RbacInvite_Valid_NoErrors() =>
        Assert.Empty(RbacInviteValidator.Instance.Evaluate(ValidInvite()));

    [Fact]
    public void RbacInvite_MissingEmail_BlocksOnEmail()
    {
        var error = Assert.Single(
            RbacInviteValidator.Instance.Evaluate(ValidInvite() with { Email = "  " }),
            e => e.FieldKey == RbacInviteFieldKeys.Email);
        Assert.Equal(ConsoleValidationSeverity.Blocker, error.Severity);
    }

    [Fact]
    public void RbacInvite_BadEmail_ErrorsOnEmail()
    {
        // An '@'-bearing identity is held to the email rule.
        var error = Assert.Single(
            RbacInviteValidator.Instance.Evaluate(ValidInvite() with { Email = "bad@nodot" }),
            e => e.FieldKey == RbacInviteFieldKeys.Email);
        Assert.Equal("rbac.invite.email.format", error.Code);
    }

    [Fact]
    public void RbacInvite_GroupIdentity_IsAccepted()
    {
        // A non-email group principal (no '@') is accepted; the drawer is labeled "Email or group".
        Assert.DoesNotContain(
            RbacInviteValidator.Instance.Evaluate(ValidInvite() with { Email = "group:operators" }),
            e => e.FieldKey == RbacInviteFieldKeys.Email);
    }

    [Fact]
    public void RbacInvite_WhitespaceInIdentity_Errors()
    {
        var error = Assert.Single(
            RbacInviteValidator.Instance.Evaluate(ValidInvite() with { Email = "two words" }),
            e => e.FieldKey == RbacInviteFieldKeys.Email);
        Assert.Equal("rbac.invite.email.format", error.Code);
    }

    [Fact]
    public void RbacInvite_NoScopeSelected_BlocksOnScope()
    {
        var state = ValidInvite() with { ScopeDev = false, ScopeStaging = false, ScopeProd = false };
        Assert.Contains(
            RbacInviteValidator.Instance.Evaluate(state),
            e => e.FieldKey == RbacInviteFieldKeys.Scope && e.Severity == ConsoleValidationSeverity.Blocker);
    }

    [Fact]
    public void RbacInvite_ValidCidr_NoAllowlistError()
    {
        var state = ValidInvite() with { IpAllowlist = "10.0.0.0/8, 192.168.0.0/16\n2001:db8::/32" };
        Assert.DoesNotContain(
            RbacInviteValidator.Instance.Evaluate(state),
            e => e.FieldKey == RbacInviteFieldKeys.IpAllowlist);
    }

    [Fact]
    public void RbacInvite_InvalidCidr_ErrorsOnAllowlist()
    {
        var state = ValidInvite() with { IpAllowlist = "10.0.0.0/8, not-a-cidr" };
        var error = Assert.Single(
            RbacInviteValidator.Instance.Evaluate(state),
            e => e.FieldKey == RbacInviteFieldKeys.IpAllowlist);
        Assert.Equal("rbac.invite.ipAllowlist.cidr", error.Code);
    }

    // --- Share manage ---

    private static ShareManageState ValidShare(DateTimeOffset now) => new("item-123", now.AddDays(7), now);

    [Fact]
    public void Share_Valid_NoErrors()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.Empty(ShareManageValidator.Instance.Evaluate(ValidShare(now)));
    }

    [Fact]
    public void Share_NoExpiry_IsValid()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.Empty(ShareManageValidator.Instance.Evaluate(new ShareManageState("item-123", ExpiresAt: null, now)));
    }

    [Fact]
    public void Share_PastExpiry_ErrorsOnExpiry()
    {
        var now = DateTimeOffset.UtcNow;
        var error = Assert.Single(
            ShareManageValidator.Instance.Evaluate(new ShareManageState("item-123", now.AddDays(-1), now)),
            e => e.FieldKey == ShareManageFieldKeys.ExpiresAt);
        Assert.Equal("share.expiresAt.future", error.Code);
    }

    [Fact]
    public void Share_MalformedItemId_ErrorsOnItemId()
    {
        var now = DateTimeOffset.UtcNow;
        var error = Assert.Single(
            ShareManageValidator.Instance.Evaluate(new ShareManageState("bad id", now.AddDays(1), now)),
            e => e.FieldKey == ShareManageFieldKeys.ItemId);
        Assert.Equal("share.itemId.format", error.Code);
    }

    // --- Operate temporal diff selection ---

    private static TemporalCheckpoint Checkpoint(string id, DateTimeOffset createdAt) =>
        new(id, "src-1", TemporalCursorType.Timestamp, id, id, createdAt, null, null, null, null);

    [Fact]
    public void TemporalDiff_OrderedSelection_NoErrors()
    {
        // Checkpoints are listed newest-first: index 0 = newer ("now"), index 1 = older ("as-of").
        var newer = Checkpoint("cp-new", DateTimeOffset.UtcNow);
        var older = Checkpoint("cp-old", DateTimeOffset.UtcNow.AddDays(-2));
        // from = older (index 1), to = newer (index 0) -> ordered.
        var state = new TemporalDiffSelection("cp-old", "cp-new", new[] { newer, older });
        Assert.Empty(TemporalDiffSelectionValidator.Instance.Evaluate(state));
    }

    [Fact]
    public void TemporalDiff_FromAfterTo_FlagsOrderError()
    {
        var newer = Checkpoint("cp-new", DateTimeOffset.UtcNow);
        var older = Checkpoint("cp-old", DateTimeOffset.UtcNow.AddDays(-2));
        // from = newer (index 0), to = older (index 1) -> inverted by list position.
        var state = new TemporalDiffSelection("cp-new", "cp-old", new[] { newer, older });
        var error = Assert.Single(
            TemporalDiffSelectionValidator.Instance.Evaluate(state),
            e => e.FieldKey == TemporalDiffFieldKeys.From);
        Assert.Equal("temporal.diff.order", error.Code);
    }

    [Fact]
    public void TemporalDiff_SameCheckpoint_IsOrdered()
    {
        var cp = Checkpoint("cp", DateTimeOffset.UtcNow);
        var state = new TemporalDiffSelection("cp", "cp", new[] { cp });
        Assert.Empty(TemporalDiffSelectionValidator.Instance.Evaluate(state));
    }

    [Fact]
    public void TemporalDiff_MissingSelection_Blocks()
    {
        var state = new TemporalDiffSelection(null, null, Array.Empty<TemporalCheckpoint>());
        var errors = TemporalDiffSelectionValidator.Instance.Evaluate(state);
        Assert.Contains(errors, e => e.FieldKey == TemporalDiffFieldKeys.From && e.Severity == ConsoleValidationSeverity.Blocker);
        Assert.Contains(errors, e => e.FieldKey == TemporalDiffFieldKeys.To && e.Severity == ConsoleValidationSeverity.Blocker);
    }

    // --- Operate publishing lookup ---

    [Fact]
    public void Publishing_ValidLookupId_NoErrors() =>
        Assert.Empty(PublishingLookupValidator.Instance.Evaluate(new PublishingLookupState("pub-123", RepublishTitle: null)));

    [Fact]
    public void Publishing_RepublishTitle_IsNeverValidated() =>
        Assert.Empty(PublishingLookupValidator.Instance.Evaluate(new PublishingLookupState("pub-123", "any free text title!!")));

    [Fact]
    public void Publishing_MissingLookupId_Blocks()
    {
        var error = Assert.Single(
            PublishingLookupValidator.Instance.Evaluate(new PublishingLookupState("  ", null)),
            e => e.FieldKey == PublishingLookupFieldKeys.LookupId);
        Assert.Equal(ConsoleValidationSeverity.Blocker, error.Severity);
    }

    [Fact]
    public void Publishing_MalformedLookupId_Errors()
    {
        var error = Assert.Single(
            PublishingLookupValidator.Instance.Evaluate(new PublishingLookupState("bad id", null)),
            e => e.FieldKey == PublishingLookupFieldKeys.LookupId);
        Assert.Equal("publishing.lookupId.format", error.Code);
    }

    // --- Environment profile ---

    private static EnvironmentProfileState ValidProfile() =>
        new("Honua Prod", "https://prod.honua.example", MtlsEnabled: false, CertValue: null);

    [Fact]
    public void Environment_Valid_NoErrors() =>
        Assert.Empty(EnvironmentProfileValidator.Instance.Evaluate(ValidProfile()));

    [Fact]
    public void Environment_MissingDisplayName_Blocks()
    {
        var error = Assert.Single(
            EnvironmentProfileValidator.Instance.Evaluate(ValidProfile() with { DisplayName = "  " }),
            e => e.FieldKey == EnvironmentProfileFieldKeys.DisplayName);
        Assert.Equal(ConsoleValidationSeverity.Blocker, error.Severity);
    }

    [Theory]
    [InlineData("http://insecure.example")]
    [InlineData("prod.honua.example")]
    [InlineData("")]
    public void Environment_NonHttpsServerUri_Blocks(string uri)
    {
        var error = Assert.Single(
            EnvironmentProfileValidator.Instance.Evaluate(ValidProfile() with { ServerBaseUri = uri }),
            e => e.FieldKey == EnvironmentProfileFieldKeys.ServerBaseUri);
        Assert.Equal("environment.serverBaseUri.https", error.Code);
    }

    [Fact]
    public void Environment_MtlsWithoutCert_Blocks()
    {
        var state = ValidProfile() with { MtlsEnabled = true, CertValue = null };
        var error = Assert.Single(
            EnvironmentProfileValidator.Instance.Evaluate(state),
            e => e.FieldKey == EnvironmentProfileFieldKeys.CertValue);
        Assert.Equal("environment.certValue.requiredWhenMtls", error.Code);
    }

    [Fact]
    public void Environment_MtlsWithCert_IsValid()
    {
        var state = ValidProfile() with { MtlsEnabled = true, CertValue = "CN=Honua Operator" };
        Assert.Empty(EnvironmentProfileValidator.Instance.Evaluate(state));
    }
}
