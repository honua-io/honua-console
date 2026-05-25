using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Images;
using DotNet.Testcontainers.Networks;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.PostgreSql;

namespace Honua.Console.Native.Core.Tests;

public sealed class OperateObservabilityTestcontainersTests
{
    private const string ServerImageEnvironmentVariable = "HONUA_CONSOLE_OPERATE_SERVER_IMAGE";
    private const string ServerContextEnvironmentVariable = "HONUA_CONSOLE_OPERATE_SERVER_CONTEXT";
    private const string ServerDockerfileEnvironmentVariable = "HONUA_CONSOLE_OPERATE_SERVER_DOCKERFILE";

    [SkippableFact]
    [Trait("Category", "Testcontainers")]
    public async Task OperatePageRendersLiveHonuaServerDataFromTestcontainers()
    {
        var suffix = Guid.NewGuid().ToString("N");
        string? serverImage = null;
        IFutureDockerImage? builtServerImage = null;
        var database = "honua_console_operate";
        var username = "honua";
        var password = "honua_password";
        var network = new NetworkBuilder()
            .WithName($"honua-console-operate-{suffix}")
            .Build();
        PostgreSqlContainer? postgres = null;
        IContainer? server = null;

        try
        {
            (serverImage, builtServerImage) = await ResolveServerImageAsync(suffix);
            await network.CreateAsync();
            postgres = new PostgreSqlBuilder("postgis/postgis:16-3.4")
                .WithDatabase(database)
                .WithUsername(username)
                .WithPassword(password)
                .WithNetwork(network)
                .WithNetworkAliases("postgres")
                .Build();
            await postgres.StartAsync();

            server = new ContainerBuilder(serverImage!)
                .WithNetwork(network)
                .WithPortBinding(8080, assignRandomHostPort: true)
                .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Test")
                .WithEnvironment("HONUA_DEV_AUTH", "true")
                .WithEnvironment("HONUA_DEV_AUTH_ALLOW_BYPASS", "true")
                .WithEnvironment("Alerts__Enabled", "true")
                .WithEnvironment("Alerts__Edition", "Enterprise")
                .WithEnvironment(
                    "ConnectionStrings__DefaultConnection",
                    $"Host=postgres;Port=5432;Database={database};Username={username};Password={password}")
                .WithWaitStrategy(Wait.ForUnixContainer()
                    .UntilHttpRequestIsSucceeded(request => request
                        .ForPort(8080)
                        .ForPath("/healthz/live")))
                .Build();
            await server.StartAsync();

            var baseUri = new Uri($"http://localhost:{server.GetMappedPublicPort(8080).ToString(System.Globalization.CultureInfo.InvariantCulture)}");
            using var seedClient = new HttpClient { BaseAddress = baseUri };
            await SeedOperateFixtureAsync(seedClient);
            await SeedRecentLogEvidenceAsync(seedClient);
            await AssertOperateSurfaceAvailableAsync(seedClient, "/api/v1/admin/observability/events", "events");
            await AssertOperateSurfaceAvailableAsync(seedClient, "/api/v1/admin/observability/alerts", "alerts");
            await AssertOperateSurfaceAvailableAsync(seedClient, "/api/v1/admin/jobs", "jobs");
            await AssertOperateSurfaceAvailableAsync(seedClient, "/api/v1/admin/observability/logs", "logs");

            var operateClient = new HttpConsoleOperateObservabilityClient(
                new HttpClient(),
                new InMemoryConsoleEnvironmentProfileStore(
                    [
                        new ConsoleEnvironmentProfile
                        {
                            Id = "testcontainers",
                            DisplayName = "Testcontainers Honua Server",
                            ServerBaseUri = baseUri,
                            Account = new ConsoleAccountBinding
                            {
                                AuthMode = ConsoleAccountAuthMode.AccountRbac,
                                AccountId = "operator.testcontainers"
                            }
                        }
                    ],
                    activeProfileId: "testcontainers"),
                new InMemoryConsoleAccountSessionStore());

            var services = new ServiceCollection();
            services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
            services.AddSingleton<IConsoleOperateObservabilityClient>(operateClient);
            var provider = services.BuildServiceProvider();

            await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
            var html = await renderer.Dispatcher.InvokeAsync(async () =>
            {
                var output = await renderer.RenderComponentAsync<OperateObservabilityPage>();
                return output.ToHtmlString();
            });

            Assert.Contains("Testcontainers Honua Server", html);
            Assert.Contains("alert_rule.create", html);
            Assert.Contains("minSeverity", html);
            Assert.Contains("Harbor Entry Testcontainers", html);
            Assert.Contains("Honolulu Harbor Testcontainers", html);
            Assert.Contains("Live investigation Testcontainers", html);
            Assert.DoesNotContain("OperateObservabilityFixture.Default", html);
            Assert.DoesNotContain("job-publish-001", html);
        }
        catch (Exception ex) when (IsDockerUnavailable(ex))
        {
            throw new SkipException($"Docker is unavailable for Testcontainers: {ex.Message}");
        }
        finally
        {
            if (server is not null)
            {
                await server.DisposeAsync();
            }

            if (postgres is not null)
            {
                await postgres.DisposeAsync();
            }

            await network.DisposeAsync();

            if (builtServerImage is not null)
            {
                await builtServerImage.DeleteAsync();
            }
        }
    }

    private static async Task<(string Image, IFutureDockerImage? BuiltImage)> ResolveServerImageAsync(string suffix)
    {
        var configuredImage = Environment.GetEnvironmentVariable(ServerImageEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(configuredImage))
        {
            return (configuredImage, null);
        }

        var contextDirectory = Environment.GetEnvironmentVariable(ServerContextEnvironmentVariable);
        Skip.If(
            string.IsNullOrWhiteSpace(contextDirectory),
            $"{ServerImageEnvironmentVariable} is not set and {ServerContextEnvironmentVariable} is not set. Set one of them to run this integration test against a honua-server build containing admin Operate endpoints.");
        Skip.If(
            !Directory.Exists(contextDirectory),
            $"{ServerContextEnvironmentVariable} points to a directory that does not exist: {contextDirectory}");

        var dockerfile = Environment.GetEnvironmentVariable(ServerDockerfileEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(dockerfile))
        {
            dockerfile = "Dockerfile";
        }

        var imageName = $"honua-console-operate-server:{suffix}";
        var image = new ImageFromDockerfileBuilder()
            .WithName(imageName)
            .WithContextDirectory(contextDirectory!)
            .WithDockerfile(dockerfile)
            .WithDeleteIfExists(true)
            .Build();
        await image.CreateAsync();
        return (imageName, image);
    }

    private static async Task SeedOperateFixtureAsync(HttpClient client)
    {
        var serviceId = $"console-{Guid.NewGuid():N}";
        var zoneResponse = await client.PostAsJsonAsync("/api/v1/admin/alerts/zones", new
        {
            serviceId,
            zoneName = "Honolulu Harbor Testcontainers",
            wkt = "POLYGON((-157.88 21.29,-157.88 21.31,-157.85 21.31,-157.85 21.29,-157.88 21.29))",
            srid = 4326,
            metadata = new Dictionary<string, string?> { ["owner"] = "Console integration" },
            isActive = true
        });
        await EnsureSuccessAsync(zoneResponse, "create geofence zone");

        var zone = await ReadDataElementAsync(zoneResponse);
        var zoneId = zone.GetProperty("zoneId").GetInt64();
        var ruleResponse = await client.PostAsJsonAsync("/api/v1/admin/alerts/rules", new
        {
            serviceId,
            layerId = 1,
            zoneId = (long?)zoneId,
            ruleName = "Harbor Entry Testcontainers",
            triggerType = "enter",
            conditionsJson = "{\"speedKmh\":30}",
            cooldownSeconds = 60,
            severity = "warning",
            editionRequired = "enterprise",
            channels = new[] { "websocket" },
            isActive = true
        });
        await EnsureSuccessAsync(ruleResponse, "create realtime rule");

        var investigationResponse = await client.PostAsJsonAsync("/api/v1/admin/investigations", new
        {
            title = "Live investigation Testcontainers",
            summary = "Seeded by honua-console Operate Testcontainers integration."
        });
        await EnsureSuccessAsync(investigationResponse, "create investigation");

        var investigation = await ReadRootElementAsync(investigationResponse);
        var investigationId = investigation.GetProperty("investigationId").GetString();
        Assert.False(string.IsNullOrWhiteSpace(investigationId));

        var pinResponse = await client.PostAsJsonAsync($"/api/v1/admin/investigations/{Uri.EscapeDataString(investigationId!)}/pins", new
        {
            eventRef = "audit:testcontainers",
            eventKind = "audit",
            occurredAt = DateTimeOffset.UtcNow,
            note = "Pinned seeded audit event."
        });
        await EnsureSuccessAsync(pinResponse, "pin event to investigation");

        var linkResponse = await client.PostAsJsonAsync($"/api/v1/admin/investigations/{Uri.EscapeDataString(investigationId!)}/links", new
        {
            resourceKind = "job",
            resourceId = "testcontainers-job",
            note = "Linked synthetic job evidence from seeded fixture."
        });
        await EnsureSuccessAsync(linkResponse, "link investigation resource");
    }

    private static async Task SeedRecentLogEvidenceAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/api/v1/admin/observability/events?minSeverity=3");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static async Task AssertOperateSurfaceAvailableAsync(HttpClient client, string path, string surface)
    {
        using var response = await client.GetAsync(path);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            Assert.Fail($"Operate {surface} surface was unavailable at {path}: {(int)response.StatusCode} {response.ReasonPhrase}. {body}");
        }
    }

    private static async Task<JsonElement> ReadDataElementAsync(HttpResponseMessage response)
    {
        var root = await ReadRootElementAsync(response);
        return root.GetProperty("data").Clone();
    }

    private static async Task<JsonElement> ReadRootElementAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, string operation)
    {
        if (response.StatusCode is < HttpStatusCode.OK or >= HttpStatusCode.MultipleChoices)
        {
            var body = await response.Content.ReadAsStringAsync();
            Assert.Fail($"Failed to {operation}: {(int)response.StatusCode} {response.ReasonPhrase}. {body}");
        }
    }

    private static bool IsDockerUnavailable(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            var name = current.GetType().FullName ?? current.GetType().Name;
            if (name.Contains("DockerUnavailable", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (current.Message.Contains("Docker is either not running", StringComparison.OrdinalIgnoreCase)
                || current.Message.Contains("Cannot connect to the Docker daemon", StringComparison.OrdinalIgnoreCase)
                || current.Message.Contains("error during connect", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
