using System.Net;
using System.Net.Http.Json;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;

namespace Honua.Console.Shell.Tests;

public sealed class FileImportCoordinateSystemTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PreviewRequiresTheServerCoordinateSystemContract(bool hasCoordinateSystem)
    {
        using var handler = new ImportHandler { HasCoordinateSystem = hasCoordinateSystem };
        using var http = new HttpClient(handler);
        using var client = Client(http);
        var operation = new HonuaServerConsoleFileImportOperation(client, new Authority());
        var result = await operation.PreviewAsync([1, 2, 3], "projected.gpkg");
        Assert.Equal(hasCoordinateSystem, result.Succeeded);
        Assert.Equal("/prefix/api/v1/admin/import/preview", handler.Path);
        Assert.Equal("operator-environment", handler.Binding);
        if (hasCoordinateSystem)
        {
            Assert.Equal(2193, result.CoordinateSystem!.DetectedSrid);
            Assert.Equal("NZGD2000 / New Zealand Transverse Mercator 2000", result.CoordinateSystem.DetectedCrs!.Name);
            Assert.Equal("operator-environment", result.AuthorityBinding);
        }
        else
        {
            Assert.Null(result.AuthorityBinding);
        }
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(null, 4326)]
    [InlineData(2193, null)]
    [InlineData(2193, 4326)]
    public async Task ImportSendsOnlyExplicitSourceAndTargetChoices(int? source, int? target)
    {
        using var handler = new ImportHandler();
        using var http = new HttpClient(handler);
        using var client = Client(http);
        var operation = new HonuaServerConsoleFileImportOperation(client, new Authority());
        var result = await operation.ImportAsync(Command(source, target));
        Assert.True(result.Succeeded, result.Detail);
        Assert.Equal("/prefix/api/v1/admin/import/upload", handler.Path);
        Assert.Equal("operator-environment", handler.Binding);
        Assert.Equal(source?.ToString(), handler.Fields.GetValueOrDefault("SourceSrid"));
        Assert.Equal(target?.ToString(), handler.Fields.GetValueOrDefault("TargetSrid"));
        Assert.Equal("projected", handler.Fields["TableName"]);
        Assert.Equal("public", handler.Fields["TargetSchema"]);
        Assert.Equal(target ?? source ?? 2193, result.StoredSrid);
        Assert.Equal(target is not null && target != (source ?? 2193), result.Reprojected);
    }

    [Fact]
    public async Task ChangedAuthorityRefusesImportBeforeTransport()
    {
        using var handler = new ImportHandler();
        using var http = new HttpClient(handler);
        using var client = Client(http);
        var operation = new HonuaServerConsoleFileImportOperation(client, new Authority { Current = false });
        var result = await operation.ImportAsync(Command(null, null));
        Assert.False(result.Succeeded);
        Assert.Null(handler.Path);
    }

    [Fact]
    public async Task ChangedAuthorityDiscardsPreview()
    {
        var authority = new Authority();
        using var handler = new ImportHandler { OnSend = () => authority.Current = false };
        using var http = new HttpClient(handler);
        using var client = Client(http);
        var result = await new HonuaServerConsoleFileImportOperation(client, authority).PreviewAsync([1], "projected.gpkg");
        Assert.False(result.Succeeded);
        Assert.Null(result.CoordinateSystem);
        Assert.Null(result.AuthorityBinding);
    }

    [Fact]
    public async Task LostImportResponseMustNotInviteRetry()
    {
        using var handler = new ImportHandler { LoseResponse = true };
        using var http = new HttpClient(handler);
        using var client = Client(http);
        var result = await new HonuaServerConsoleFileImportOperation(client, new Authority()).ImportAsync(Command(null, null));
        Assert.False(result.Succeeded);
        Assert.Equal(ConsoleWriteOutcome.UnverifiedState, result.State);
        Assert.Contains("original", result.Detail);
    }

    [Fact]
    public async Task LookupIsBoundEncodedAndLimited()
    {
        using var handler = new ImportHandler();
        using var http = new HttpClient(handler);
        using var client = Client(http);
        var result = await new HonuaServerConsoleFileImportOperation(client, new Authority())
            .SearchCoordinateSystemsAsync("NZGD2000 / New Zealand", "operator-environment");
        Assert.True(result.IsAllowed, result.Message);
        Assert.Equal(2193, Assert.Single(result.Value!.Items).Code);
        Assert.Equal("/prefix/api/v1/admin/coordinate-systems?q=NZGD2000%20%2F%20New%20Zealand&limit=20", handler.Path);
        Assert.Equal("operator-environment", handler.Binding);
    }

    [Theory]
    [InlineData(0, null)]
    [InlineData(null, -1)]
    public async Task InvalidCoordinateChoicesNeverReachTransport(int? source, int? target)
    {
        using var handler = new ImportHandler();
        using var http = new HttpClient(handler);
        using var client = Client(http);
        var result = await new HonuaServerConsoleFileImportOperation(client, new Authority()).ImportAsync(Command(source, target));
        Assert.False(result.Succeeded);
        Assert.Null(handler.Path);
    }

    [Fact]
    public async Task ImportWithoutReviewedAuthorityCannotRun()
    {
        using var handler = new ImportHandler();
        using var http = new HttpClient(handler);
        using var client = Client(http);
        var result = await new HonuaServerConsoleFileImportOperation(client, new Authority())
            .ImportAsync(Command(null, null) with { AuthorityBinding = null });
        Assert.False(result.Succeeded);
        Assert.Null(handler.Path);
    }

    [Fact]
    public async Task ChangedAuthorityAfterImportLeavesTheOriginalOutcomeUnverified()
    {
        var authority = new Authority();
        using var handler = new ImportHandler { OnSend = () => authority.Current = false };
        using var http = new HttpClient(handler);
        using var client = Client(http);
        var result = await new HonuaServerConsoleFileImportOperation(client, authority).ImportAsync(Command(null, null));
        Assert.Equal(ConsoleWriteOutcome.UnverifiedState, result.State);
        Assert.NotNull(handler.Path);
    }

    [Theory]
    [InlineData(403)]
    [InlineData(503)]
    public async Task ServerRefusalPreservesItsReason(int status)
    {
        using var handler = new ImportHandler { Status = (HttpStatusCode)status };
        using var http = new HttpClient(handler);
        using var client = Client(http);
        var operation = new HonuaServerConsoleFileImportOperation(client, new Authority());
        var result = await operation.PreviewAsync([1], "projected.gpkg");
        Assert.False(result.Succeeded);
        Assert.Contains("registry unavailable", result.Detail);
        Assert.Null(result.AuthorityBinding);
    }

    [Fact]
    public async Task ChangedAuthorityDiscardsLookupResults()
    {
        var authority = new Authority();
        using var handler = new ImportHandler { OnSend = () => authority.Current = false };
        using var http = new HttpClient(handler);
        using var client = Client(http);
        var result = await new HonuaServerConsoleFileImportOperation(client, authority)
            .SearchCoordinateSystemsAsync("2193", "operator-environment");
        Assert.False(result.IsAllowed);
        Assert.Null(result.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task EmptyLookupDoesNotCallTransport(string query)
    {
        using var handler = new ImportHandler();
        using var http = new HttpClient(handler);
        using var client = Client(http);
        var result = await new HonuaServerConsoleFileImportOperation(client, new Authority())
            .SearchCoordinateSystemsAsync(query, "operator-environment");
        Assert.False(result.IsAllowed);
        Assert.Null(handler.Path);
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("{}")]
    [InlineData("null")]
    public async Task MalformedImportResponseDoesNotInviteAnotherMutation(string payload)
    {
        using var handler = new ImportHandler { RawResponse = payload };
        using var http = new HttpClient(handler);
        using var client = Client(http);
        var result = await new HonuaServerConsoleFileImportOperation(client, new Authority()).ImportAsync(Command(null, null));
        Assert.False(result.Succeeded);
        Assert.Equal(ConsoleWriteOutcome.UnverifiedState, result.State);
        Assert.Contains("original", result.Detail);
    }

    [Theory]
    [InlineData(2193, 2193, false)]
    [InlineData(3857, 4326, true)]
    [InlineData(2193, 4326, false)]
    public async Task CompletedImportWithDifferentCoordinatesCannotInviteRetry(int source, int stored, bool reprojected)
    {
        var payload = System.Text.Json.JsonSerializer.Serialize(new
        { success = true, sourceSrid = source, storedSrid = stored, reprojected, tableName = "projected" });
        using var handler = new ImportHandler { RawResponse = payload };
        using var http = new HttpClient(handler);
        using var client = Client(http);

        var result = await new HonuaServerConsoleFileImportOperation(client, new Authority()).ImportAsync(Command(null, 4326));

        Assert.False(result.Succeeded);
        Assert.Contains("different coordinate system", result.State, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("original", result.Detail);
    }

    [Fact]
    public async Task NullPreviewWarningsCannotCrashTheImportPage()
    {
        const string payload = """{"coordinateSystem":{"detectedSrid":2193,"sourceCrsDeclared":true,"detectionMethod":"file-metadata","effectiveSourceSrid":2193,"storedSrid":2193,"reprojected":false,"requiresSourceOverride":false},"warnings":null}""";
        using var handler = new ImportHandler { RawResponse = payload };
        using var http = new HttpClient(handler);
        using var client = Client(http);

        var result = await new HonuaServerConsoleFileImportOperation(client, new Authority()).PreviewAsync([1], "projected.gpkg");

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Warnings);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public async Task ExplicitTargetCannotInventAnUnknownSource()
    {
        using var handler = new ImportHandler();
        using var http = new HttpClient(handler);
        using var client = Client(http);

        var result = await new HonuaServerConsoleFileImportOperation(client, new Authority())
            .ImportAsync(Command(null, 4326) with { ExpectedSourceSrid = null });

        Assert.False(result.Succeeded);
        Assert.Null(handler.Path);
    }

    private static HonuaAdminOperateHttpClient Client(HttpClient http) => new(http, new(new Uri("https://fixture.invalid/prefix/")));

    private static ConsoleFileImportCommand Command(int? source, int? target) => new()
    {
        FileContent = [1, 2, 3],
        FileName = "projected.gpkg",
        TableName = "projected",
        TargetSchema = "public",
        AuthorityBinding = "operator-environment",
        SourceSrid = source,
        TargetSrid = target,
        ExpectedSourceSrid = 2193
    };

    private sealed class Authority : IConsoleAuthorityBindingValidator
    {
        public bool Current { get; set; } = true;
        public Task<string?> CaptureAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>("operator-environment");
        public Task<bool> IsCurrentAsync(string binding, CancellationToken cancellationToken = default) => Task.FromResult(Current && binding == "operator-environment");
    }

    private sealed class ImportHandler : HttpMessageHandler
    {
        public string? RawResponse { get; init; }
        public HttpStatusCode Status { get; init; } = HttpStatusCode.OK;
        public bool HasCoordinateSystem { get; init; } = true;
        public bool LoseResponse { get; init; }
        public Action? OnSend { get; init; }
        public string? Path { get; private set; }
        public string? Binding { get; private set; }
        public Dictionary<string, string> Fields { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Path = request.RequestUri!.PathAndQuery;
            Binding = request.Options.TryGetValue(ConsoleAuthorityRequestOptions.Expected, out var binding) ? binding : null;
            if (request.Content is MultipartFormDataContent form)
                foreach (var part in form)
                    Fields[part.Headers.ContentDisposition!.Name!.Trim('"')] = await part.ReadAsStringAsync(cancellationToken);
            OnSend?.Invoke();
            if (LoseResponse) throw new HttpRequestException("response lost");
            if (RawResponse is { } raw) return new(HttpStatusCode.OK) { Content = new StringContent(raw) };
            if (Status != HttpStatusCode.OK) return new(Status) { Content = JsonContent.Create(new { message = "registry unavailable" }) };
            if (Path.Contains("coordinate-systems", StringComparison.Ordinal))
                return Json(new { items = new[] { Crs }, hasMore = false });
            if (Path.EndsWith("preview", StringComparison.Ordinal))
                return Json(new
                {
                    coordinateSystem = HasCoordinateSystem ? new
                    {
                        detectedSrid = 2193,
                        sourceCrsDeclared = true,
                        detectionMethod = "file-metadata",
                        detectedCrs = Crs,
                        effectiveSourceSrid = 2193,
                        targetSrid = (int?)null,
                        storedSrid = 2193,
                        reprojected = false,
                        requiresSourceOverride = false
                    } : null
                });
            var sourceSrid = Fields.TryGetValue("SourceSrid", out var source) ? int.Parse(source, System.Globalization.CultureInfo.InvariantCulture) : 2193;
            var storedSrid = Fields.TryGetValue("TargetSrid", out var target) ? int.Parse(target, System.Globalization.CultureInfo.InvariantCulture) : sourceSrid;
            return Json(new { success = true, featureCount = 2, tableName = "projected", sourceSrid, storedSrid, reprojected = sourceSrid != storedSrid });
        }

        private static object Crs => new { code = 2193, name = "NZGD2000 / New Zealand Transverse Mercator 2000", unit = "metre", area = "New Zealand" };
        private static HttpResponseMessage Json(object body) => new(HttpStatusCode.OK) { Content = JsonContent.Create(body) };
    }
}
