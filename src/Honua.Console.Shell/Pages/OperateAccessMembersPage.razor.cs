using Honua.Console.Contracts;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Validation;
using Microsoft.AspNetCore.Components;

namespace Honua.Console.Shell.Pages;

public partial class OperateAccessMembersPage
{
    private TeamMembershipView? _membership;
    private RbacOverviewView? _overview;
    private RbacCapabilityState? _bindingState;
    private bool _retiredEnvironmentLink;
    private bool _loading = true;
    private bool _inviteOpen;

    private string? _inviteEmail;
    private string _inviteSendVia = "Email · OIDC sign-in link";
    private string? _inviteRoleId;
    private bool _scopeDev = true;
    private bool _scopeStaging;
    private bool _scopeProd;
    private string _inviteExpires = "in 30 days · renew required";
    private bool _inviteMfaRequired = true;
    private string? _inviteIpAllowlist;

    // Client-side validation for the scoped-invite drawer. There is no server body-validator for invite
    // (catalog: "RBAC / share: no dedicated body validators"), so this is purely client-side and surfaced inline.
    private readonly ValidationState _inviteValidation = new();

    private RbacInviteState BuildInviteState() =>
        new(_inviteEmail, _scopeDev, _scopeStaging, _scopeProd, _inviteIpAllowlist);

    private void OnInviteEdit() =>
        _inviteValidation.SetClientErrors(RbacInviteValidator.Instance.Evaluate(BuildInviteState()));

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    [SupplyParameterFromQuery(Name = "environment")]
    private string? Environment { get; set; }

    private string EnvironmentId => string.IsNullOrWhiteSpace(Environment) ? "current" : Environment!;

    private RbacRoleView? SelectedRole =>
        _overview?.Roles.FirstOrDefault(role => string.Equals(role.Id, _inviteRoleId, StringComparison.Ordinal))
        ?? _overview?.Roles.FirstOrDefault();

    private string AccessHref(string path) => string.IsNullOrWhiteSpace(Environment)
        ? path
        : $"{path}?environment={Uri.EscapeDataString(Environment!)}";

    private string? _environmentId;
    private int _generation;
    private bool _disposed;

    private bool IsCurrent(int generation) => !_disposed && generation == _generation;

    protected override async Task OnParametersSetAsync()
    {
        if (ConsoleUrlQuery.HasRetiredEnvironmentKey(Navigation.Uri))
        {
            _retiredEnvironmentLink = true;
            _loading = false;
            _membership = null;
            _overview = null;
            _bindingState = null;
            _inviteOpen = false;
            _environmentId = null;
            _generation++;
            return;
        }

        _retiredEnvironmentLink = false;
        if (string.Equals(_environmentId, EnvironmentId, StringComparison.Ordinal)) return;
        _environmentId = EnvironmentId;
        var generation = ++_generation;
        var environment = _environmentId;
        ResetEnvironment();
        var load = await RbacData.LoadMembershipAsync(environment);
        if (!IsCurrent(generation)) return;
        _membership = load.Membership;
        _bindingState = load.CapabilityStates.FirstOrDefault();
        if (load.HasMembership)
        {
            var overviewLoad = await RbacData.LoadOverviewAsync(environment);
            if (!IsCurrent(generation)) return;
            _overview = overviewLoad.Overview;
            _inviteRoleId = _overview?.Roles.FirstOrDefault()?.Id;
        }
        _loading = false;
    }

    private void ResetEnvironment()
    {
        _loading = true;
        _bindingState = null;
        _membership = null;
        _overview = null;
        _inviteOpen = false;
        _inviteEmail = null;
        _inviteRoleId = null;
        _inviteSendVia = "Email · OIDC sign-in link";
        _scopeDev = true;
        _scopeStaging = false;
        _scopeProd = false;
        _inviteExpires = "in 30 days · renew required";
        _inviteMfaRequired = true;
        _inviteIpAllowlist = null;
        OnInviteEdit();
    }

    public void Dispose()
    {
        _disposed = true;
        _generation++;
    }

    private void OpenInvite()
    {
        _inviteOpen = true;
        // Seed findings so the form gates from first render (e.g. no scope / missing email).
        OnInviteEdit();
    }

    private void CloseInvite() => _inviteOpen = false;

    private string PermissionLabel(string key) =>
        _overview?.Permissions.FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.Ordinal))?.Label ?? key;
}
