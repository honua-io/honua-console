using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;

namespace Honua.Console.Native.Core.Tests;

public sealed class OperateTransitionDataSourceTests
{
    [Fact]
    public void RedactorRemovesCredentialsTokensConnectionStringsAndSecretValues()
    {
        const string raw = """
            connectionString=Server=db.internal;Database=land;User Id=svc;Password=hunter2;
            api_key=abc123
            Authorization: Bearer eyJhbGciOiJIUzI1NiJ9.payload.signature
            secret://connections/prod-postgres/password-value-from-vault
            """;

        var redacted = OperateSecretRedactor.Redact(raw);

        Assert.DoesNotContain("hunter2", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("abc123", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("eyJhbGciOiJIUzI1NiJ9", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("Server=db.internal", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("password-value-from-vault", redacted, StringComparison.Ordinal);
        Assert.Contains("connectionString=[redacted]", redacted, StringComparison.Ordinal);
        Assert.Contains("api_key=[redacted]", redacted, StringComparison.Ordinal);
        Assert.Contains("Authorization: Bearer [redacted]", redacted, StringComparison.Ordinal);
        Assert.Contains("secret://connections/prod-postgres/[redacted]", redacted, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FailedConnectionDiagnosticIsStructuredActionableAndSafe()
    {
        var dataSource = InMemoryOperateTransitionDataSource.CreateSeeded();
        var connection = await dataSource.FindConnectionAsync("prod-postgres");

        var diagnostic = connection?.LastDiagnostic;

        Assert.NotNull(diagnostic);
        Assert.Equal("AUTH_INVALID_CREDENTIAL", diagnostic.FailureCode);
        Assert.NotEmpty(diagnostic.Signals);
        Assert.NotEmpty(diagnostic.OperatorActions);
        Assert.NotEmpty(diagnostic.Evidence);

        var renderedText = string.Join(
            " ",
            new[]
            {
                diagnostic.Summary,
                string.Join(" ", diagnostic.Signals.Select(signal => signal.Message)),
                string.Join(" ", diagnostic.OperatorActions),
                string.Join(" ", diagnostic.Evidence.Select(entry => $"{entry.Key}={entry.Value}"))
            });

        Assert.DoesNotContain("wrong-value", renderedText, StringComparison.Ordinal);
        Assert.DoesNotContain("eyJhbGciOiJIUzI1NiJ9", renderedText, StringComparison.Ordinal);
        Assert.DoesNotContain("Host=pg-prod.internal.honua", renderedText, StringComparison.Ordinal);
        Assert.DoesNotContain("vault-material", renderedText, StringComparison.Ordinal);
        Assert.Contains("secret://connections/prod-postgres/[redacted]", renderedText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DataSourceReturnsOnlySafeDiagnostics()
    {
        var dataSource = InMemoryOperateTransitionDataSource.CreateSeeded();

        var workspace = await dataSource.GetWorkspaceAsync();
        var connection = await dataSource.FindConnectionAsync("prod-postgres");

        Assert.NotNull(connection);
        Assert.Same(connection, workspace.Connections.Single(candidate => candidate.Id == "prod-postgres"));
        AssertDiagnosticIsSafe(connection.LastDiagnostic);
    }

    [Fact]
    public async Task ResourceEditCarriesValidationStateAndBlastRadius()
    {
        var dataSource = InMemoryOperateTransitionDataSource.CreateSeeded();
        var resource = await dataSource.FindResourceEditAsync("res-parcels-2026");

        Assert.NotNull(resource);
        Assert.Contains("Blocked", resource.ValidationState, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(resource.ValidationIssues, issue => issue.Severity == "Error");
        Assert.NotEmpty(resource.BlastRadius.CatalogItems);
        Assert.NotEmpty(resource.BlastRadius.Services);
        Assert.NotEmpty(resource.BlastRadius.Layers);
        Assert.NotEmpty(resource.BlastRadius.SavedMaps);
        Assert.NotEmpty(resource.BlastRadius.ShareLinks);
        Assert.NotEmpty(resource.BlastRadius.GeneratedApps);
        Assert.Contains("Validation", resource.EditTabs);
        Assert.Contains("Advanced", resource.EditTabs);
    }

    [Fact]
    public async Task ServicesExposeLayersButPointMetadataToCanonicalResources()
    {
        var dataSource = InMemoryOperateTransitionDataSource.CreateSeeded();
        var service = await dataSource.FindServiceAsync("planning-feature-service");

        Assert.NotNull(service);
        Assert.Contains("owned by data resources", service.MetadataOwnership, StringComparison.OrdinalIgnoreCase);
        Assert.All(service.Layers, layer =>
        {
            Assert.NotEqual(0, layer.LayerId);
            Assert.False(string.IsNullOrWhiteSpace(layer.CanonicalResourceId));
            Assert.False(string.IsNullOrWhiteSpace(layer.CanonicalResourceName));
        });
    }

    [Fact]
    public async Task SettingsChangesAlwaysShowApplyScopeAndRestartRequirement()
    {
        var dataSource = InMemoryOperateTransitionDataSource.CreateSeeded();
        var workspace = await dataSource.GetWorkspaceAsync();

        Assert.All(workspace.SettingsChanges, change =>
        {
            Assert.False(string.IsNullOrWhiteSpace(change.ApplyScope));
            Assert.False(string.IsNullOrWhiteSpace(change.RestartRequirement));
            Assert.False(string.IsNullOrWhiteSpace(change.PolicyState));
        });
        Assert.Contains(workspace.SettingsChanges, change => change.Id == "cors" && change.RequiresRestart);
    }

    private static void AssertDiagnosticIsSafe(OperateConnectionDiagnostic? diagnostic)
    {
        Assert.NotNull(diagnostic);

        var renderedText = string.Join(
            " ",
            new[]
            {
                diagnostic.Summary,
                string.Join(" ", diagnostic.Signals.Select(signal => signal.Message)),
                string.Join(" ", diagnostic.OperatorActions),
                string.Join(" ", diagnostic.Evidence.Select(entry => $"{entry.Key}={entry.Value}"))
            });

        Assert.DoesNotContain("wrong-value", renderedText, StringComparison.Ordinal);
        Assert.DoesNotContain("eyJhbGciOiJIUzI1NiJ9", renderedText, StringComparison.Ordinal);
        Assert.DoesNotContain("Host=pg-prod.internal.honua", renderedText, StringComparison.Ordinal);
        Assert.DoesNotContain("vault-material", renderedText, StringComparison.Ordinal);
        Assert.Contains("secret://connections/prod-postgres/[redacted]", renderedText, StringComparison.Ordinal);
    }
}
