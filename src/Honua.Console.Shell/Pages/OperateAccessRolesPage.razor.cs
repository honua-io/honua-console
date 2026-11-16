using Honua.Console.Contracts;
using Honua.Console.Shell.Components;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;
using Microsoft.AspNetCore.Components;

namespace Honua.Console.Shell.Pages;

public partial class OperateAccessRolesPage
{
    private RbacOverviewView? _overview;
    private RbacCapabilityState? _bindingState;
    private bool _loading = true;

    private bool _showCreate;
    private bool _showAudit;
    private bool _saving;
    private string _newRoleName = string.Empty;
    private string _newRoleDescription = string.Empty;
    private readonly HashSet<string> _newRoleGrants = new(StringComparer.Ordinal);
    private string? _mutationError;
    private ConsoleConfirmDialog? _confirm;
    private IReadOnlyList<RbacAuditEntryView> _audit = [];
    private RbacCapabilityState? _auditIssue;

    [SupplyParameterFromQuery(Name = "workspace")]
    private string? Workspace { get; set; }

    private string WorkspaceId => string.IsNullOrWhiteSpace(Workspace) ? "current" : Workspace!;

    private string? _workspaceId;
    private int _generation;
    private int _auditRequest;
    private bool _disposed;

    private bool IsCurrent(int generation) => !_disposed && generation == _generation;

    protected override async Task OnParametersSetAsync()
    {
        if (string.Equals(_workspaceId, WorkspaceId, StringComparison.Ordinal)) return;
        _workspaceId = WorkspaceId;
        var generation = ++_generation;
        _loading = true;
        _bindingState = null;
        _overview = null;
        _showCreate = false;
        _showAudit = false;
        _saving = false;
        _mutationError = null;
        _audit = [];
        _auditIssue = null;
        await ReloadOverviewAsync(generation);
        if (IsCurrent(generation)) _loading = false;
    }

    private async Task ReloadOverviewAsync(int generation)
    {
        if (!IsCurrent(generation)) return;
        var load = await RbacData.LoadOverviewAsync(_workspaceId!);
        if (!IsCurrent(generation)) return;
        _overview = load.Overview;
        _bindingState = load.HasOverview ? null : load.CapabilityStates.FirstOrDefault();
    }

    public void Dispose()
    {
        _disposed = true;
        _generation++;
    }

    private void ToggleCreate()
    {
        if (_saving || _overview?.CanManageRoles != true) return;
        _showCreate = !_showCreate;
        _mutationError = null;
        if (_showCreate)
        {
            _newRoleName = string.Empty;
            _newRoleDescription = string.Empty;
            _newRoleGrants.Clear();
            // Default a sensible baseline grant so a new role is never empty.
            _newRoleGrants.Add("view");
        }
    }

    private void ToggleGrant(string permissionKey, bool granted)
    {
        if (_saving) return;
        if (granted)
        {
            _newRoleGrants.Add(permissionKey);
        }
        else
        {
            _newRoleGrants.Remove(permissionKey);
        }
    }

    private Task SubmitCreateAsync()
    {
        if (string.IsNullOrWhiteSpace(_newRoleName) || _saving || _overview?.CanManageRoles != true)
        {
            return Task.CompletedTask;
        }

        var generation = _generation;
        var workspace = _workspaceId!;
        _mutationError = null;
        return RunMutationAsync(generation,
            () => CreateRoleAsync(generation, workspace), "Couldn't create the role");
    }

    private async Task CreateRoleAsync(int generation, string workspace)
    {
        var result = await RbacData.CreateRoleAsync(
            workspace, _newRoleName.Trim(),
            string.IsNullOrWhiteSpace(_newRoleDescription) ? null : _newRoleDescription.Trim(),
            _newRoleGrants.ToArray());
        if (!IsCurrent(generation)) return;
        if (!result.Succeeded)
        {
            _mutationError = result.Error ?? "The role could not be created.";
            return;
        }
        _showCreate = false;
        await ReloadOverviewAsync(generation);
        if (IsCurrent(generation) && _showAudit) await LoadAuditAsync();
    }

    private void ConfirmDeleteRole(string roleId, string roleName)
    {
        if (_confirm is null || _saving || _overview?.CanManageRoles != true) return;
        var generation = _generation;
        _confirm.Open(
            title: $"Delete role '{roleName}'?",
            onConfirm: () => DeleteRoleAsync(roleId, roleName, generation),
            body: $"This permanently removes the custom role '{roleName}' and revokes any grants using it. This cannot be undone.",
            confirmLabel: "Delete role");
    }

    private Task DeleteRoleAsync(string roleId, string roleName, int generation)
    {
        if (_saving || !IsCurrent(generation) || _overview?.CanManageRoles != true) return Task.CompletedTask;
        var workspace = _workspaceId!;
        _mutationError = null;
        return RunMutationAsync(generation,
            async () =>
            {
                var result = await RbacData.DeleteRoleAsync(workspace, roleId);
                if (!IsCurrent(generation)) return;
                if (result.Succeeded)
                {
                    await ReloadOverviewAsync(generation);
                    if (IsCurrent(generation) && _showAudit)
                    {
                        await LoadAuditAsync();
                    }
                    if (IsCurrent(generation)) Notifications.Success($"Role '{roleName}' deleted.");
                }
                else
                {
                    _mutationError = result.Error ?? $"Role '{roleName}' could not be deleted.";
                }
            },
            $"Couldn't delete role '{roleName}'");
    }

    private async Task ToggleAuditAsync()
    {
        _showAudit = !_showAudit;
        if (_showAudit)
        {
            await LoadAuditAsync();
        }
    }

    private async Task LoadAuditAsync()
    {
        var generation = _generation;
        var request = ++_auditRequest;
        var load = await RbacData.LoadAuditAsync(_workspaceId!);
        if (!IsCurrent(generation) || request != _auditRequest || !_showAudit) return;
        _audit = load.Entries;
        _auditIssue = load.FirstIssue;
    }

    private Task<bool> RunMutationAsync(int generation, Func<Task> operation, string failureMessage) =>
        Notifications.RunGuardedAsync(
            async () =>
            {
                try { await operation(); }
                catch (Exception) when (!IsCurrent(generation)) { }
            },
            setBusy: busy =>
            {
                if (!IsCurrent(generation)) return;
                _saving = busy;
                StateHasChanged();
            },
            failureMessage: failureMessage);

    private static RenderFragment RenderGrant(RbacGrantView grant) => builder =>
    {
        if (grant.IsNotGranted)
        {
            builder.OpenElement(0, "span");
            builder.AddAttribute(1, "class", "rbac-dash");
            builder.AddContent(2, "—");
            builder.CloseElement();
            return;
        }

        switch (grant.Grant)
        {
            case HonuaConsolePermissionGrants.Scoped:
                builder.OpenElement(0, "span");
                builder.AddAttribute(1, "class", "console-status console-state-info rbac-badge-scoped");
                builder.AddContent(2, "scoped");
                builder.CloseElement();
                break;
            case HonuaConsolePermissionGrants.EnvScoped:
                builder.OpenElement(0, "span");
                builder.AddAttribute(1, "class", "console-status console-state-neutral rbac-badge-env");
                builder.AddContent(2, "env-scoped");
                builder.CloseElement();
                break;
            case HonuaConsolePermissionGrants.Custom:
                builder.OpenElement(0, "span");
                builder.AddAttribute(1, "class", "console-status console-state-info rbac-badge-custom");
                builder.AddContent(2, string.IsNullOrWhiteSpace(grant.Qualifier) ? "●" : grant.Qualifier);
                builder.CloseElement();
                break;
            default:
                builder.OpenElement(0, "span");
                builder.AddAttribute(1, "class", "console-status console-state-success rbac-dot-granted-badge");
                builder.AddContent(2, string.IsNullOrWhiteSpace(grant.Qualifier) ? "●" : grant.Qualifier);
                builder.CloseElement();
                break;
        }
    };
}
