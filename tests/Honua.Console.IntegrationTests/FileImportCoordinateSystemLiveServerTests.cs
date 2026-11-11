using System.Text;
using Bunit;
using Honua.Console.Contracts;
using Honua.Console.Shell.Components;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Honua.Console.IntegrationTests;

[Collection(ServiceLayerPublishIntegrationCollection.Name)]
public sealed class FileImportCoordinateSystemLiveServerTests(ServiceLayerPublishFixture fixture)
{
    [SkippableTheory]
    [InlineData(null, 2193)]
    [InlineData(4326, 4326)]
    public async Task ExplicitSourceAndOptionalTargetControlTheStoredGeometry(int? target, int expectedStored)
    {
        Skip.If(fixture.SkipReason is not null, fixture.SkipReason ?? "");
        using var http = fixture.CreateHttpClient();
        using var client = new HonuaAdminOperateHttpClient(http, new(fixture.BaseAddress, fixture.Options.StudioAdminApiKey));
        var operation = new HonuaServerConsoleFileImportOperation(client, new FixtureAuthority());
        var bytes = Encoding.UTF8.GetBytes("id,name,wkt\n1,projected,\"POINT(1740000 5420000)\"\n");
        var preview = await operation.PreviewAsync(bytes, "projected.csv");
        Assert.True(preview.Succeeded, preview.Detail);
        Skip.If(preview.CoordinateSystem is not { RequiresSourceOverride: true },
            "This server image does not ask for a source coordinate system.");
        Assert.Null(preview.CoordinateSystem.DetectedSrid);
        Assert.NotNull(preview.AuthorityBinding);
        var lookup = await operation.SearchCoordinateSystemsAsync("EPSG:2193", preview.AuthorityBinding);
        Assert.True(lookup.IsAllowed, lookup.Message);
        Assert.Contains(lookup.Value!.Items, item => item.Code == 2193 && !string.IsNullOrWhiteSpace(item.Name));

        using var context = new BunitContext();
        context.Services.AddSingleton<IConsoleFileImportOperation>(operation);
        var step = context.Render<ImportCoordinateSystemStep>(parameters => parameters
            .Add(component => component.CoordinateSystem, preview.CoordinateSystem)
            .Add(component => component.AuthorityBinding, preview.AuthorityBinding));
        Assert.Contains("source coordinate system", step.Markup, StringComparison.OrdinalIgnoreCase);

        var table = $"crs_import_{Guid.NewGuid():N}";
        var result = await operation.ImportAsync(new ConsoleFileImportCommand
        {
            FileContent = bytes,
            FileName = "projected.csv",
            TableName = table,
            TargetSchema = "public",
            SourceSrid = 2193,
            TargetSrid = target,
            AuthorityBinding = preview.AuthorityBinding
        });
        Assert.True(result.Succeeded, result.Detail);
        Assert.Equal(expectedStored, result.StoredSrid);
        Assert.Equal(target.HasValue, result.Reprojected);
        await using var database = new NpgsqlConnection(fixture.PostgresConnectionString);
        await database.OpenAsync();
        await using var command = new NpgsqlCommand($"SELECT ST_SRID(geom) FROM public.\"{table}\"", database);
        Assert.Equal(expectedStored, Convert.ToInt32(await command.ExecuteScalarAsync()));
    }

    private sealed class FixtureAuthority : IConsoleAuthorityBindingValidator
    {
        public Task<string?> CaptureAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>("test-server");
        public Task<bool> IsCurrentAsync(string binding, CancellationToken cancellationToken = default) => Task.FromResult(binding == "test-server");
    }
}
