using Honua.Console.Contracts;
using Honua.Console.Shell.Components;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.Shell.Pages;

public partial class OperateApiKeysPage
{
    private IHonuaAdminOperateClient? _client;
    private IConsoleAuthorityBindingValidator? _authority;
    private IConsoleEnvironmentProfileStore? _profiles;
    private ConsoleConfirmDialog? _confirm;
    private IReadOnlyList<HonuaAdminApiKeyResponse> _keys = [];
    private HonuaAdminEndpointIssue? _issue;
    private string? _binding;
    private string? _secret;
    private string _name = "";
    private string _permissions = "";
    private DateTime? _expiry;
    private bool _busy;
    private bool _loading;
    private bool _needsRefresh;
    private bool _disposed;
    private int _generation;

    protected override async Task OnInitializedAsync()
    {
        _client = Services.GetService<IHonuaAdminOperateClient>();
        _authority = Services.GetService<IConsoleAuthorityBindingValidator>();
        _profiles = Services.GetService<IConsoleEnvironmentProfileStore>();
        if (_profiles is not null) _profiles.ActiveProfileChanged += ProfileChanged;
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        if (_busy) return;
        ClearSensitiveState();
        if (Services.GetService<ConsoleV4Options>()?.Enabled != true || _client is null || _authority is null)
        {
            _issue = new("Unsupported", "API keys", "Operator-bound API-key management is unavailable in this Console environment.");
            return;
        }
        _busy = _loading = true;
        var generation = _generation;
        try
        {
            var binding = await _authority.CaptureAsync();
            if (binding is null) { TargetChanged(); return; }
            var result = await _client.ListApiKeysBoundAsync(binding);
            if (generation != _generation || !await CurrentAsync(binding)) return;
            _issue = result.Issue;
            _binding = result.Issue is null && result.Data is not null ? binding : null;
            _keys = result.Data ?? [];
            _needsRefresh = false;
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException)
        {
            _issue = new("Unavailable", "API keys", "The server could not be reached. Refresh to try reading keys again.");
        }
        finally { _busy = _loading = false; }
    }

    private async Task<bool> CurrentAsync(string binding)
    {
        if (_disposed) return false;
        if (_authority is not null && await _authority.IsCurrentAsync(binding)) return !_disposed;
        TargetChanged();
        return false;
    }

    private async Task CreateAsync()
    {
        if (_busy || _needsRefresh || _secret is not null || _binding is not { } binding || _client is null) return;
        var permissions = _permissions.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (string.IsNullOrWhiteSpace(_name) || permissions.Length == 0)
        {
            _issue = new("Invalid request", "API keys", "Enter a name and at least one permission.");
            return;
        }
        _busy = true;
        _needsRefresh = true;
        _issue = null;
        var generation = _generation;
        try
        {
            if (!await CurrentAsync(binding) || generation != _generation) return;
            var request = new HonuaAdminCreateApiKeyRequest(_name.Trim(), permissions)
            {
                ExpiresAt = _expiry is { } expiry ? new DateTimeOffset(DateTime.SpecifyKind(expiry, DateTimeKind.Utc)) : null
            };
            var result = await _client.CreateApiKeyAsync(new(binding, request));
            if (generation != _generation || !await CurrentAsync(binding)) return;
            AcceptCreatedKey(result);
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException) { Unverified(); }
        finally { _busy = false; }
    }

    private void AcceptCreatedKey(HonuaAdminEndpointResult<HonuaAdminCreatedApiKey> result)
    {
        _issue = result.Issue;
        if (result.Issue is null && result.Data is { } created && !string.IsNullOrWhiteSpace(created.Key))
        {
            _keys = [created.ApiKey, .. _keys];
            _secret = created.Key;
            _name = _permissions = "";
            _expiry = null;
            _needsRefresh = false;
        }
        else _issue ??= new("Outcome unverified", "API keys", "The server did not return the new secret. Refresh the list and revoke the key before creating a replacement.");
    }

    private void ConfirmRevoke(HonuaAdminApiKeyResponse key)
    {
        if (_busy || _needsRefresh || _binding is not { } binding) return;
        _confirm?.Open($"Revoke {key.Name}?", () => RevokeAsync(key.Id, binding),
            "Applications using this key will lose access. This cannot be undone.");
    }

    private async Task RevokeAsync(Guid id, string binding)
    {
        if (_busy || _needsRefresh || _binding != binding || _client is null) return;
        _busy = true;
        _needsRefresh = true;
        _secret = null;
        _issue = null;
        var generation = _generation;
        try
        {
            if (!await CurrentAsync(binding) || generation != _generation) return;
            var result = await _client.RevokeApiKeyAsync(new(binding, id));
            if (generation != _generation || !await CurrentAsync(binding)) return;
            _issue = result.Issue;
            if (result.Issue is null && result.Data is { } revoked)
            {
                _keys = _keys.Select(key => key.Id == id ? revoked : key).ToArray();
                _needsRefresh = false;
            }
            else _issue ??= new("Outcome unverified", "API keys", "Refresh the list to confirm whether the key was revoked before trying again.");
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException) { Unverified(); }
        finally { _busy = false; }
    }

    private void Unverified() => _issue = new(ConsoleWriteOutcome.UnverifiedState, "API keys", ConsoleWriteOutcome.UnverifiedDetail);
    private void DismissSecret() => _secret = null;
    private static string Expiry(DateTimeOffset? expiry) => expiry?.ToUniversalTime().ToString("u", System.Globalization.CultureInfo.InvariantCulture) ?? "Never";

    private void ProfileChanged() => _ = InvokeAsync(() => { TargetChanged(); StateHasChanged(); });

    private void TargetChanged()
    {
        ClearSensitiveState();
        _issue = new("Target changed", "API keys", "The operator or environment changed. Refresh before managing keys. Confirm any pending change on the original server before repeating it.");
    }

    private void ClearSensitiveState()
    {
        _generation++;
        _secret = _binding = null;
        _keys = [];
        _name = _permissions = "";
        _expiry = null;
        _issue = null;
    }

    public void Dispose()
    {
        _disposed = true;
        ClearSensitiveState();
        if (_profiles is not null) _profiles.ActiveProfileChanged -= ProfileChanged;
    }
}
