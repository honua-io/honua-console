using Bunit;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

public sealed class ConsoleSettingsV4Tests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RolloutPreservesReadOnlyValuesAndSourceEndpoints(bool enabled)
    {
        using var context = Context(enabled, new([new("api", "Server", "Admin API version", "1", "Server runtime", false, "None", "/api/v1/admin/version")], []));
        var page = context.Render<OperateSettingsPage>();
        Assert.Equal("Settings", page.Find("h1").TextContent);
        Assert.Equal(["Category", "Setting", "Current value", "Scope", "Read from"], page.FindAll("th").Select(cell => cell.TextContent));
        Assert.Equal(["Server", "Admin API version", "1", "Server runtime", "/api/v1/admin/version"], page.FindAll("tbody td").Select(cell => cell.TextContent));
        Assert.Empty(page.FindAll("input,select,textarea,button"));
        Assert.Equal(enabled ? 1 : 0, page.FindAll(".console-page-heading-v4").Count);
        Assert.Equal(enabled ? 1 : 0, page.FindAll("#server-configuration-heading").Count);
        Assert.Equal(1, context.Services.GetRequiredService<SettingsSource>().Reads);
        CatalogAdminPreview.Capture($"settings-{(enabled ? "v4" : "legacy")}", $"<div class=\"console-shell\" data-console-v4=\"{enabled.ToString().ToLowerInvariant()}\">{page.Markup}</div>");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EmptyConfigurationRetainsExistingEmptyState(bool enabled)
    {
        using var context = Context(enabled, new([], []));
        var page = context.Render<OperateSettingsPage>();
        Assert.Contains("runtime settings", page.Markup);
        Assert.Empty(page.FindAll("table"));
        Assert.Empty(page.FindAll("input,select,textarea,button"));
    }

    [Theory]
    [InlineData(false, "forbidden")]
    [InlineData(true, "forbidden")]
    [InlineData(false, "unavailable")]
    [InlineData(true, "unavailable")]
    public void UnreadableConfigurationKeepsCapabilityEvidenceAndSurfaceFiltering(bool enabled, string state)
    {
        using var context = Context(enabled, new([], [new("Settings", state, "GET /api/v1/admin/version", "Settings could not be read."), new("Operate", "missing", "Server binding", "No server is bound."), new("Services", "unavailable", "Services contract", "Unrelated Services diagnostic.")]));
        var page = context.Render<OperateSettingsPage>();
        Assert.Contains(state, page.Markup);
        Assert.Contains("Settings could not be read.", page.Markup);
        Assert.Contains("GET /api/v1/admin/version", page.Markup);
        Assert.Contains("No server is bound.", page.Markup);
        Assert.DoesNotContain("Unrelated Services diagnostic.", page.Markup);
        Assert.Empty(page.FindAll("table"));
    }

    private static BunitContext Context(bool enabled, OperateSettingsView view)
    {
        var context = new BunitContext();
        context.Services.AddSingleton(new ConsoleV4Options { Enabled = enabled });
        context.Services.AddSingleton(new SettingsSource(view));
        context.Services.AddSingleton<IOperateTransitionDataSource>(provider => provider.GetRequiredService<SettingsSource>());
        return context;
    }

    private sealed class SettingsSource(OperateSettingsView view) : IOperateTransitionDataSource
    {
        public int Reads { get; private set; }
        public Task<OperateSettingsView> GetSettingsViewAsync(CancellationToken cancellationToken = default)
        {
            Reads++;
            return Task.FromResult(view);
        }
        public Task<OperateTransitionWorkspace> GetWorkspaceAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<OperateConnectionSummary?> FindConnectionAsync(string connectionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<OperateResourceEditPreview?> FindResourceEditAsync(string resourceId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<OperateServiceDetail?> FindServiceAsync(string serviceName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
