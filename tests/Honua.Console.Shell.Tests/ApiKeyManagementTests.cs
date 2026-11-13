using System.Net;
using System.Text;
using System.Text.Json;
using Bunit;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.Shell.Tests;

public sealed class ApiKeyManagementTests
{
    [Fact]
    public void EmptyListAndCreateRevealTheSecretOnlyUntilDismissed()
    {
        using var fixture = new Fixture();
        var page = fixture.Context.Render<OperateApiKeysPage>();
        page.WaitForAssertion(() => Assert.Contains("No API keys", page.Markup));
        V4PagePreview.Capture("api-keys-empty", page.Markup);
        page.Find("#api-key-name").Change("Reporting app");
        page.Find("#api-key-permissions").Change("admin:read, services:read");
        page.Find("form").Submit();
        page.WaitForAssertion(() => Assert.Equal("one-time-secret", page.Find("#api-key-secret").GetAttribute("value")));
        Assert.Equal("operator-a", fixture.Handler.Binding);
        using var body = JsonDocument.Parse(fixture.Handler.Body!);
        Assert.Equal("Reporting app", body.RootElement.GetProperty("name").GetString());
        Assert.Equal(2, body.RootElement.GetProperty("permissions").GetArrayLength());
        V4PagePreview.Capture("api-keys-created", page.Markup);
        page.FindAll("button").Single(button => button.TextContent == "I saved the key").Click();
        Assert.DoesNotContain("one-time-secret", page.Markup);
        Assert.Contains("Reporting app", page.Markup);
        V4PagePreview.Capture("api-keys-list", page.Markup);
    }

    [Fact]
    public void RevocationNeedsConfirmationAndUsesTheOriginalKeyId()
    {
        using var fixture = new Fixture();
        fixture.Handler.HasKey = true;
        var page = fixture.Context.Render<OperateApiKeysPage>();
        page.FindAll("button").Single(button => button.TextContent.StartsWith("Revoke")).Click();
        Assert.Equal(0, fixture.Handler.Mutations);
        Assert.Contains("Applications using this key will lose access", page.Markup);
        page.Find("[data-console-confirm-accept]").Click();
        page.WaitForAssertion(() => Assert.Equal(1, fixture.Handler.Mutations));
        Assert.EndsWith($"/{Fixture.KeyId:D}/revoke", fixture.Handler.Path);
        Assert.Contains("revoked", page.Markup);
    }

    [Fact]
    public void PermissionFailureIsVisibleAndDoesNotInventAnEmptyList()
    {
        using var fixture = new Fixture();
        fixture.Handler.Status = HttpStatusCode.Forbidden;
        var page = fixture.Context.Render<OperateApiKeysPage>();
        page.WaitForAssertion(() => Assert.Contains("403", page.Markup));
        Assert.Empty(page.FindAll("form"));
        Assert.DoesNotContain("No API keys", page.Markup);
        V4PagePreview.Capture("api-keys-denied", page.Markup);
    }

    [Fact]
    public async Task EnvironmentChangeDuringCreateDiscardsTheSecretAndRequiresRefresh()
    {
        using var fixture = new Fixture();
        var completion = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Handler.Pending = completion.Task;
        var page = fixture.Context.Render<OperateApiKeysPage>();
        page.Find("#api-key-name").Change("Reporting app");
        page.Find("#api-key-permissions").Change("admin:read");
        var submit = page.Find("form").SubmitAsync();
        page.WaitForAssertion(() => Assert.Equal(1, fixture.Handler.Mutations));
        fixture.Authority.Current = false;
        completion.SetResult(ApiHandler.Json(new { success = true, data = new { apiKey = Fixture.Key, key = "must-not-display" } }));
        await submit;
        page.WaitForAssertion(() => Assert.Contains("Target changed", page.Markup));
        Assert.DoesNotContain("must-not-display", page.Markup);
        Assert.Empty(page.FindAll("form"));
    }

    [Fact]
    public void FailedWriteRequiresReadingTheListBeforeAnotherAttempt()
    {
        using var fixture = new Fixture();
        var page = fixture.Context.Render<OperateApiKeysPage>();
        fixture.Handler.Status = HttpStatusCode.Forbidden;
        page.Find("#api-key-name").Change("Reporting app");
        page.Find("#api-key-permissions").Change("admin:read");
        page.Find("form").Submit();
        page.WaitForAssertion(() => Assert.Contains("403", page.Markup));
        Assert.True(page.Find("button[type=submit]").HasAttribute("disabled"));
        page.Find("form").Submit();
        Assert.Equal(1, fixture.Handler.Mutations);
    }

    [Fact]
    public void MissingBindingFailsClosed()
    {
        using var context = new BunitContext();
        var page = context.Render<OperateApiKeysPage>();
        Assert.Contains("Unsupported", page.Markup);
        Assert.Empty(page.FindAll("form"));
    }

    private static readonly Guid KeyId = Guid.Parse("f99f8453-9782-466a-848d-bb05f48672a0");
    private static HonuaAdminApiKeyResponse Key => new() { Id = KeyId, Name = "Reporting app", KeyPrefix = "hn_test", Permissions = ["admin:read"], Status = "active" };

    private sealed class Fixture : IDisposable
    {
        public BunitContext Context { get; } = new();
        public ApiHandler Handler { get; } = new();
        public Authority Authority { get; } = new();
        public static Guid KeyId => ApiKeyManagementTests.KeyId;
        public static HonuaAdminApiKeyResponse Key => ApiKeyManagementTests.Key;
        private readonly HonuaAdminOperateHttpClient _client;

        public Fixture()
        {
            var uri = new Uri("https://server.example");
            _client = new(new HttpClient(Handler), new(uri));
            Context.Services.AddSingleton(new ConsoleV4Options { Enabled = true });
            Context.Services.AddSingleton<IHonuaAdminOperateClient>(_client);
            Context.Services.AddSingleton<IConsoleAuthorityBindingValidator>(Authority);
        }
        public void Dispose() { Context.Dispose(); _client.Dispose(); }
    }

    private sealed class Authority : IConsoleAuthorityBindingValidator
    {
        public bool Current { get; set; } = true;
        public Task<string?> CaptureAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>("operator-a");
        public Task<bool> IsCurrentAsync(string binding, CancellationToken cancellationToken = default) => Task.FromResult(Current && binding == "operator-a");
    }

    private sealed class ApiHandler : HttpMessageHandler
    {
        public bool HasKey { get; set; }
        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
        public Task<HttpResponseMessage>? Pending { get; set; }
        public int Mutations { get; private set; }
        public string? Binding { get; private set; }
        public string? Body { get; private set; }
        public string? Path { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            request.Options.TryGetValue(ConsoleAuthorityRequestOptions.Expected, out var binding);
            Binding = binding;
            Path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Get)
                return Status == HttpStatusCode.OK ? Json(new { success = true, data = HasKey ? new[] { Key } : [] }) : new(Status);
            Mutations++;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            if (Pending is not null) return await Pending;
            if (Status != HttpStatusCode.OK) return new(Status);
            return Path.EndsWith("/revoke", StringComparison.Ordinal)
                ? Json(new { success = true, data = Key with { Status = "revoked", RevokedAt = DateTimeOffset.UtcNow } })
                : Json(new { success = true, data = new { apiKey = Key, key = "one-time-secret" } });
        }

        public static HttpResponseMessage Json<T>(T body) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(body, new JsonSerializerOptions(JsonSerializerDefaults.Web)), Encoding.UTF8, "application/json")
        };
    }
}
