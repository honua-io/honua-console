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
    private bool _retiredEnvironmentLink;
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

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    [SupplyParameterFromQuery(Name = "environment")]
    private string? Environment { get; set; }

    private string EnvironmentId => string.IsNullOrWhiteSpace(Environment) ? "current" : Environment!;

    private string AccessHref(string path) => string.IsNullOrWhiteSpace(Environment)
        ? path
        : $"{path}?environment={Uri.EscapeDataString(Environment!)}";

    private string? _environmentId;
    private int _generation;
    private int _auditRequest;
    private bool _disposed;

    private bool IsCurrent(int generation) => !_disposed && generation == _generation;

    protected override async Task OnParametersSetAsync()
    {
        if (ConsoleRetiredEnvironmentQuery.HasRetiredKey(Navigation.Uri))
        {
            _retiredEnvironmentLink = true;
            _loading = false;
            _overview = null;
            _bindingState = null;
            _showCreate = false;
            _showAudit = false;
            _audit = [];
            _auditIssue = null;
            _mutationError = null;
            _saving = false;
            _environmentId = null;
            _generation++;
            return;
        }

        _retiredEnvironmentLink = false;
        if (string.Equals(_environmentId, EnvironmentId, StringComparison.Ordinal)) return;
        _environmentId = EnvironmentId;
        await LoadEnvironmentAsync();
    }

    private Task RetryAsync() => LoadEnvironmentAsync();

    private async Task LoadEnvironmentAsync()
    {
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
        var load = await RbacData.LoadOverviewAsync(_environmentId!);
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
            // Default the server's first (baseline) permission so a new role is never empty. Only listed keys
            // may be sent: the server refuses the whole request for an unknown one, and the operator could not
            // see or untick a key that has no checkbox.
            if (_overview?.Permissions.FirstOrDefault() is { } baseline)
            {
                _newRoleGrants.Add(baseline.Key);
            }
        }
    }

    private void OnGrantChanged(string permissionKey, ChangeEventArgs change)
    {
        if (_saving) return;
        if (change.Value is true)
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
        var environment = _environmentId!;
        _mutationError = null;
        return RunMutationAsync(generation,
            () => CreateRoleAsync(generation, environment), "Couldn't create the role");
    }

    private async Task CreateRoleAsync(int generation, string environment)
    {
        var result = await RbacData.CreateRoleAsync(
            environment, _newRoleName.Trim(),
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
        var environment = _environmentId!;
        _mutationError = null;
        return RunMutationAsync(generation,
            async () =>
            {
                var result = await RbacData.DeleteRoleAsync(environment, roleId);
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
        var load = await RbacData.LoadAuditAsync(_environmentId!);
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
