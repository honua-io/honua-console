using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;
using Testcontainers.PostgreSql;

namespace Honua.Console.Native.Core.Tests;

/// <summary>
/// Opt-in Testcontainers live-server coverage for the GitOps metadata release binding.
/// Boots a real honua-server (PostGIS via Testcontainers) and drives the Console
/// <see cref="HttpConsoleGitOpsReleaseClient"/> against the SHIPPED GitOps metadata
/// release contracts (honua-server#1163 release package + GitOps manifest,
/// honua-server#1165 release-operation lifecycle).
///
/// The live assertion proves the contract is actually bound and reachable: a read of
/// an unknown release package id returns the live endpoint's 404 mapped to the
/// Console <see cref="OperateSectionStatus.Missing"/> state (endpoint present, package
/// absent) rather than the <see cref="OperateSectionStatus.Unsupported"/> state that a
/// server lacking the endpoint would yield. Skips without Docker / opt-in env.
/// </summary>
public sealed class GitOpsReleaseLiveServerTests
{
    private const string RunLiveServerTestsVariable = "HONUA_CONSOLE_RUN_LIVE_SERVER_TESTS";
    private const string HonuaServerProjectVariable = "HONUA_SERVER_PROJECT";
    private const string HonuaServerStartupTimeoutVariable = "HONUA_CONSOLE_LIVE_SERVER_STARTUP_TIMEOUT_SECONDS";
    private const string DefaultHonuaServerProject =
        "/home/makani/honua-server/src/Honua.Server/Honua.Server.csproj";

    [SkippableFact]
    [Trait("Category", "Integration")]
    public async Task GitOpsReleaseDetailBindsToLiveReleasePackageEndpoint()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable(RunLiveServerTestsVariable),
                "true",
                StringComparison.OrdinalIgnoreCase))
        {
            Skip.If(true, $"Set {RunLiveServerTestsVariable}=true to run the live honua-server Testcontainers integration test.");
        }

        var serverProject = Environment.GetEnvironmentVariable(HonuaServerProjectVariable);
        if (string.IsNullOrWhiteSpace(serverProject))
        {
            serverProject = DefaultHonuaServerProject;
        }

        if (!File.Exists(serverProject))
        {
            Skip.If(true, $"honua-server project was not found at '{serverProject}'. Set {HonuaServerProjectVariable} to a Honua.Server.csproj path.");
        }

        await using var postgres = new PostgreSqlBuilder("postgis/postgis:17-3.5-alpine")
            .WithDatabase("honua_console_releases")
            .WithUsername("honua_user")
            .WithPassword("honua_password")
            .Build();

        try
        {
            await postgres.StartAsync();
        }
        catch (Exception ex) when (LooksLikeDockerUnavailable(ex))
        {
            Skip.If(true, $"Docker is unavailable for Testcontainers: {ex.Message}");
        }

        await postgres.ExecScriptAsync("CREATE EXTENSION IF NOT EXISTS postgis;");

        using var server = await HonuaServerProcess.StartAsync(serverProject, postgres.GetConnectionString());

        var profiles = new InMemoryConsoleEnvironmentProfileStore(
            [
                new ConsoleEnvironmentProfile
                {
                    Id = "live",
                    DisplayName = "Live Server",
                    ServerBaseUri = server.BaseUri,
                    UpdatedAt = DateTimeOffset.UtcNow,
                    Account = new ConsoleAccountBinding
                    {
                        AuthMode = ConsoleAccountAuthMode.Anonymous,
                        AccountId = "operator.live"
                    }
                }
            ],
            activeProfileId: "live");

        var client = new HttpConsoleGitOpsReleaseClient(
            new HttpClient { BaseAddress = server.BaseUri },
            profiles,
            adminApiKey: null);

        // The list surface binds the live release-package list endpoint. On a fresh
        // database it returns an allowed-but-empty list (endpoint present, no packages),
        // never the Unsupported state a server lacking the endpoint would yield.
        var list = await client.GetReleaseProposalsAsync();
        Assert.Equal(OperateSectionStatus.Allowed, list.Status);
        Assert.NotNull(list.Value);

        // An unknown release package id hits the live GET endpoint, which exists and
        // returns 404 -> mapped to Missing. A server WITHOUT the contract would 404 the
        // route differently or the Console would have nothing to bind; this distinguishes
        // "endpoint present, package absent" from "endpoint absent".
        var unknownId = Guid.NewGuid().ToString("D");
        var detail = await client.GetReleaseDetailAsync(unknownId);
        Assert.Equal(OperateSectionStatus.Missing, detail.Status);
        Assert.Null(detail.Value);
    }

    private static bool LooksLikeDockerUnavailable(Exception ex)
    {
        var text = ex.ToString();
        return text.Contains("Docker", StringComparison.OrdinalIgnoreCase)
            || text.Contains("daemon", StringComparison.OrdinalIgnoreCase)
            || text.Contains("NamedPipe", StringComparison.OrdinalIgnoreCase)
            || text.Contains("/var/run/docker.sock", StringComparison.OrdinalIgnoreCase);
    }

    private sealed class HonuaServerProcess : IDisposable
    {
        private readonly Process _process;
        private readonly StringBuilder _output;

        private HonuaServerProcess(Process process, Uri baseUri, StringBuilder output)
        {
            _process = process;
            BaseUri = baseUri;
            _output = output;
        }

        public Uri BaseUri { get; }

        public static async Task<HonuaServerProcess> StartAsync(string projectPath, string connectionString)
        {
            var port = GetAvailableTcpPort();
            var baseUri = new Uri($"http://127.0.0.1:{port.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
            var output = new StringBuilder();
            var process = new Process
            {
                StartInfo = new ProcessStartInfo("dotnet")
                {
                    WorkingDirectory = Path.GetDirectoryName(projectPath)!,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false
                },
                EnableRaisingEvents = true
            };

            process.StartInfo.ArgumentList.Add("run");
            process.StartInfo.ArgumentList.Add("--project");
            process.StartInfo.ArgumentList.Add(projectPath);
            process.StartInfo.ArgumentList.Add("--no-launch-profile");
            process.StartInfo.ArgumentList.Add("--no-restore");
            process.StartInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "Test";
            process.StartInfo.Environment["HONUA_REGISTER_TEST_INFRASTRUCTURE"] = "true";
            process.StartInfo.Environment["ASPNETCORE_URLS"] = baseUri.ToString();
            process.StartInfo.Environment["ConnectionStrings__honua"] = connectionString;
            process.StartInfo.Environment["ConnectionStrings__DefaultConnection"] = connectionString;
            process.StartInfo.Environment["DataSource__Provider"] = "postgres";
            process.StartInfo.Environment["HONUA_DEV_AUTH"] = "true";
            process.StartInfo.Environment["HONUA_DEV_AUTH_ALLOW_BYPASS"] = "true";
            process.StartInfo.Environment["Geocoding__Nominatim__BaseUrl"] = "https://8.8.8.8/nominatim";
            process.StartInfo.Environment["Geocoding__Providers__Nominatim__BaseUrl"] = "https://8.8.8.8/nominatim";
            process.StartInfo.Environment["Security__ConnectionEncryption__MasterKey"] =
                "console-live-test-master-key-32-bytes";
            process.StartInfo.Environment["HONUA_OBSERVABILITY"] = "false";

            process.OutputDataReceived += (_, args) => AppendOutput(output, args.Data);
            process.ErrorDataReceived += (_, args) => AppendOutput(output, args.Data);

            if (!process.Start())
            {
                throw new InvalidOperationException("Failed to start honua-server process.");
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            var server = new HonuaServerProcess(process, baseUri, output);
            try
            {
                await server.WaitForHealthAsync().ConfigureAwait(false);
                return server;
            }
            catch
            {
                server.Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            try
            {
                if (!_process.HasExited)
                {
                    _process.Kill(entireProcessTree: true);
                }
            }
            finally
            {
                _process.Dispose();
            }
        }

        private async Task WaitForHealthAsync()
        {
            using var client = new HttpClient { BaseAddress = BaseUri };
            var timeout = GetStartupTimeout();
            var deadline = DateTimeOffset.UtcNow.Add(timeout);
            while (DateTimeOffset.UtcNow < deadline)
            {
                if (_process.HasExited)
                {
                    throw new InvalidOperationException(
                        $"honua-server exited with code {_process.ExitCode}.{Environment.NewLine}{GetCapturedOutput()}");
                }

                try
                {
                    using var response = await client.GetAsync("/healthz/live").ConfigureAwait(false);
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        return;
                    }
                }
                catch (HttpRequestException)
                {
                }

                await Task.Delay(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
            }

            throw new TimeoutException(
                $"honua-server did not become healthy at {BaseUri} within {timeout.TotalSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture)} seconds.{Environment.NewLine}{GetCapturedOutput()}");
        }

        private static TimeSpan GetStartupTimeout()
        {
            var configured = Environment.GetEnvironmentVariable(HonuaServerStartupTimeoutVariable);
            return int.TryParse(configured, out var seconds) && seconds > 0
                ? TimeSpan.FromSeconds(seconds)
                : TimeSpan.FromSeconds(300);
        }

        private static int GetAvailableTcpPort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        private static void AppendOutput(StringBuilder output, string? line)
        {
            if (!string.IsNullOrWhiteSpace(line))
            {
                output.AppendLine(line);
            }
        }

        private string GetCapturedOutput() =>
            _output.Length == 0 ? "No honua-server output was captured." : _output.ToString();
    }
}
