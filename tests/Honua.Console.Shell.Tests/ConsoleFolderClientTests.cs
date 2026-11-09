using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Honua.Sdk.Admin.Exceptions;
using Honua.Console.Contracts;
using Honua.Console.Shell.Services;
using Honua.Sdk.Admin.Models;

namespace Honua.Console.Shell.Tests;

public sealed class ConsoleFolderClientTests
{
    [Fact]
    public async Task ReadUsesPublishedFolderModelsAndCapturesCatalogAuthority()
    {
        using var handler = new FolderHandler();
        using var http = new HttpClient(handler) { BaseAddress = new("https://fixture.invalid/tenant/") };
        using var client = new ServerConsoleFolderClient(http, new Authority());
        var read = await client.ReadAsync();
        Assert.True(read.IsAllowed, read.Message);
        Assert.Equal("\"catalog-1\"", read.Value!.ETag);
        Assert.Equal("human-environment", read.Value.AuthorityBinding);
        Assert.Equal("Transport", Assert.Single(read.Value.Folders).Metadata.Name);
        Assert.Equal("/tenant/api/v1/admin/folders", Assert.Single(handler.Calls).Path);
        Assert.Equal("human-environment", handler.Calls[0].Binding);
    }

    [Theory]
    [InlineData("create", "POST", "/tenant/api/v1/admin/folders", "{\"name\":\"Transport\"}")]
    [InlineData("rename", "PATCH", "/tenant/api/v1/admin/folders/folder%20id", "{\"name\":\"Roads\",\"title\":null}")]
    [InlineData("delete", "DELETE", "/tenant/api/v1/admin/folders/folder%20id", null)]
    [InlineData("move", "PUT", "/tenant/api/v1/admin/services/logical%20service/folder", "{\"folderId\":null}")]
    public async Task MutationsRetainTypedBodyLogicalIdentityAndSnapshot(string operation, string method, string path, string? body)
    {
        using var handler = new FolderHandler();
        using var http = new HttpClient(handler) { BaseAddress = new("https://fixture.invalid/tenant/") };
        using var client = new ServerConsoleFolderClient(http, new Authority());
        var catalog = new ConsoleFolderCatalog([], "\"reviewed\"", "human-environment");
        var result = await MutateAsync(client, catalog, operation);
        Assert.True(result.Succeeded, result.Detail);
        var call = Assert.Single(handler.Calls);
        Assert.Equal(method, call.Method);
        Assert.Equal(path, call.Path);
        Assert.Equal(body, call.Body);
        Assert.Equal("\"reviewed\"", call.IfMatch);
        Assert.Equal("human-environment", call.Binding);
    }

    [Theory]
    [InlineData("create")]
    [InlineData("rename")]
    [InlineData("delete")]
    [InlineData("move")]
    public async Task ChangedHumanOrEnvironmentCannotRetargetAnEdit(string operation)
    {
        using var handler = new FolderHandler();
        using var http = new HttpClient(handler) { BaseAddress = new("https://fixture.invalid/") };
        using var client = new ServerConsoleFolderClient(http, new Authority { Current = false });
        var result = await MutateAsync(client, new([], "\"reviewed\"", "old-human-environment"), operation);
        Assert.False(result.Succeeded);
        Assert.Equal("Target changed", result.State);
        Assert.Contains("changed", result.Detail);
        Assert.Empty(handler.Calls);
    }

    [Theory]
    [InlineData(403, "Forbidden")]
    [InlineData(409, "Conflict")]
    [InlineData(412, "Snapshot changed")]
    [InlineData(428, "Snapshot required")]
    [InlineData(503, "Unavailable")]
    public async Task RefusalKeepsTheServerReasonWithoutRetry(int status, string state)
    {
        using var handler = new FolderHandler { Failure = status };
        using var http = new HttpClient(handler) { BaseAddress = new("https://fixture.invalid/") };
        using var client = new ServerConsoleFolderClient(http, new Authority());
        var result = await client.DeleteAsync(new([], "\"reviewed\"", "human-environment"), "folder id");
        Assert.False(result.Succeeded);
        Assert.Equal(state, result.State);
        Assert.Contains("The Folder still contains a Service", result.Detail);
        Assert.Single(handler.Calls);
    }

    [Fact]
    public async Task MissingETagRefusesBeforeTransportAndChangedAuthorityDiscardsTheRead()
    {
        var authority = new Authority();
        using var handler = new FolderHandler { OnSend = () => authority.Current = false };
        using var http = new HttpClient(handler) { BaseAddress = new("https://fixture.invalid/") };
        using var client = new ServerConsoleFolderClient(http, authority);
        var missing = await client.DeleteAsync(new([], null, "human-environment"), "folder id");
        Assert.False(missing.Succeeded);
        Assert.Empty(handler.Calls);
        var read = await client.ReadAsync();
        Assert.False(read.IsAllowed);
        Assert.Null(read.Value);
        Assert.Single(handler.Calls);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData(409, false)]
    [InlineData(409, true)]
    public async Task ChangedAuthorityAfterSendDoesNotClaimASafeRetry(int? status, bool markedAfterSend)
    {
        var authority = new Authority();
        using var handler = new FolderHandler { Failure = status, Unverified = markedAfterSend, OnSend = () => authority.Current = markedAfterSend };
        using var http = new HttpClient(handler) { BaseAddress = new("https://fixture.invalid/") };
        using var client = new ServerConsoleFolderClient(http, authority);
        var result = await client.DeleteAsync(new([], "\"reviewed\"", "human-environment"), "folder");
        Assert.False(result.Succeeded);
        Assert.Equal("Outcome unverified", result.State);
        Assert.Contains("original environment", result.Detail);
        Assert.Single(handler.Calls);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task UnconfirmedWriteFailuresNeverClaimNothingWasSaved(int failure)
    {
        Exception exception = failure switch
        {
            0 => new HttpRequestException("response lost"),
            1 => new OperationCanceledException(),
            2 => new JsonException("response unreadable"),
            _ => new HonuaAdminOperationException("response absent")
        };
        using var handler = new FolderHandler { Exception = exception };
        using var http = new HttpClient(handler) { BaseAddress = new("https://fixture.invalid/") };
        using var client = new ServerConsoleFolderClient(http, new Authority());
        var result = await client.DeleteAsync(new([], "\"reviewed\"", "human-environment"), "folder");
        Assert.Equal("Outcome unverified", result.State);
        Assert.Single(handler.Calls);
    }

    [Fact]
    public async Task UnreachableFolderReadShowsAnUnavailableState()
    {
        using var handler = new FolderHandler { Exception = new HttpRequestException("unreachable") };
        using var http = new HttpClient(handler) { BaseAddress = new("https://fixture.invalid/") };
        using var client = new ServerConsoleFolderClient(http, new Authority());
        var read = await client.ReadAsync();
        Assert.Equal(OperateSectionStatus.Unavailable, read.Status);
        Assert.Null(read.Value);
    }

    [Fact]
    public async Task FolderValidationKeepsTheTitleAndFieldReason()
    {
        using var handler = new FolderHandler { Failure = 400, ErrorBody = """
            {"title":"Invalid Folder","status":400,"errors":[{"code":"segment","path":"name","message":"A Folder path must be one segment."}]}
            """ };
        using var http = new HttpClient(handler) { BaseAddress = new("https://fixture.invalid/") };
        using var client = new ServerConsoleFolderClient(http, new Authority());
        var result = await client.CreateAsync(new([], "\"reviewed\"", "human-environment"), new() { Name = "bad/path" });
        Assert.Contains("Invalid Folder", result.Detail);
        Assert.Contains("one segment", result.Detail);
        Assert.Single(handler.Calls);
    }

    private static Task<ConsoleFolderMutationResult> MutateAsync(ServerConsoleFolderClient client, ConsoleFolderCatalog catalog, string operation) => operation switch
    {
        "create" => client.CreateAsync(catalog, new() { Name = "Transport" }),
        "rename" => client.UpdateAsync(catalog, "folder id", new() { Name = "Roads", Title = new(null) }),
        "delete" => client.DeleteAsync(catalog, "folder id"),
        "move" => client.MoveAsync(catalog, "logical service", new() { FolderId = null }),
        _ => throw new ArgumentOutOfRangeException(nameof(operation))
    };

    private sealed class Authority : IConsoleAuthorityBindingValidator
    {
        public bool Current { get; set; } = true;
        public Task<string?> CaptureAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>("human-environment");
        public Task<bool> IsCurrentAsync(string binding, CancellationToken cancellationToken = default) => Task.FromResult(Current && binding == "human-environment");
    }

    private sealed record Call(string Method, string Path, string? Body, string? IfMatch, string? Binding);

    private sealed class FolderHandler : HttpMessageHandler
    {
        public List<Call> Calls { get; } = [];
        public int? Failure { get; init; }
        public bool Unverified { get; init; }
        public Action? OnSend { get; init; }
        public Exception? Exception { get; init; }
        public string? ErrorBody { get; init; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls.Add(new(request.Method.Method, request.RequestUri!.AbsolutePath,
                request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken),
                request.Headers.TryGetValues("If-Match", out var match) ? match.Single() : null,
                request.Options.TryGetValue(ConsoleAuthorityRequestOptions.Expected, out var binding) ? binding : null));
            OnSend?.Invoke();
            if (Unverified) request.Options.Set(ConsoleWriteOutcome.Unverified, true);
            if (Exception is not null) throw Exception;
            if (Failure is { } failure) return new((HttpStatusCode)failure)
            {
                Content = ErrorBody is { } body ? new StringContent(body, System.Text.Encoding.UTF8, "application/problem+json") : JsonContent.Create(new { type = "urn:honua:conflict", status = failure, detail = "The Folder still contains a Service. Move it before deleting." })
            };
            var folder = new Folder { Metadata = new() { Id = "folder id", Name = "Transport" } };
            var response = new HttpResponseMessage(request.Method == HttpMethod.Get || request.Method == HttpMethod.Post || request.Method == HttpMethod.Patch ? HttpStatusCode.OK : HttpStatusCode.NoContent);
            response.Content = request.Method == HttpMethod.Get
                ? JsonContent.Create(new { success = true, data = new[] { folder } })
                : JsonContent.Create(new { success = true, data = folder });
            response.Headers.ETag = new("\"catalog-1\"");
            return response;
        }
    }
}
