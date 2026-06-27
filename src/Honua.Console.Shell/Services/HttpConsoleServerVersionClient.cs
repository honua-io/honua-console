using System.Net;
using System.Net.Http;
using System.Text.Json;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

/// <summary>
/// Binds the server-version detection used by the server-upgrade flow to a real
/// honua-server through the capability manifest (<c>GET /api/v1/capabilities/manifest</c>).
/// The manifest is anonymous; the admin API key is still attached when present so the
/// read works behind an admin-gated edge, but it is not required.
///
/// Base address comes from the active <see cref="ConsoleEnvironmentProfile"/>. Charter
/// section 11 (no standing mock for server-owned data) is preserved: this client never
/// returns seeded data; when no environment profile is bound the read returns a
/// missing-binding result. Deserialization is source-generated for trim/AOT safety and
/// ignores manifest fields outside the <c>server</c> block.
/// </summary>
public sealed class HttpConsoleServerVersionClient : IConsoleServerVersionClient
{
    private const string NoProfileMessage =
        "No active environment profile is selected. Connect an environment to detect its server version.";

    private readonly HttpClient _http;
    private readonly IConsoleEnvironmentProfileStore _profileStore;
    private readonly string? _adminApiKey;

    public HttpConsoleServerVersionClient(
        HttpClient http,
        IConsoleEnvironmentProfileStore profileStore,
        string? adminApiKey = null)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _profileStore = profileStore ?? throw new ArgumentNullException(nameof(profileStore));
        _adminApiKey = string.IsNullOrWhiteSpace(adminApiKey) ? null : adminApiKey;
    }

    public async Task<OperateSectionResult<ServerVersionInfo>> GetServerVersionAsync(
        CancellationToken cancellationToken = default)
    {
        var profile = await _profileStore.GetActiveProfileAsync(cancellationToken).ConfigureAwait(false);
        if (profile is null)
        {
            return OperateSectionResult<ServerVersionInfo>.Denied(
                OperateSectionStatus.Unavailable,
                NoProfileMessage);
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            BuildUri(profile.ServerBaseUri, ServerVersionAdminRoutes.Manifest()));
        if (!string.IsNullOrWhiteSpace(_adminApiKey))
        {
            request.Headers.TryAddWithoutValidation("X-API-Key", _adminApiKey);
        }

        try
        {
            using var response = await _http
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return OperateSectionResult<ServerVersionInfo>.Denied(
                    MapStatus(response.StatusCode),
                    MapErrorMessage(response.StatusCode));
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var envelope = await JsonSerializer.DeserializeAsync(
                stream,
                ServerVersionJsonContext.Default.CapabilityManifestServerEnvelope,
                cancellationToken).ConfigureAwait(false);

            if (envelope?.Server is not { } server || string.IsNullOrWhiteSpace(server.ServerVersion))
            {
                return OperateSectionResult<ServerVersionInfo>.Denied(
                    OperateSectionStatus.Unavailable,
                    "The connected server's capability manifest did not report a server version.");
            }

            return OperateSectionResult<ServerVersionInfo>.Allowed(new ServerVersionInfo(
                ServerVersion: server.ServerVersion,
                ApiVersion: server.ApiVersion,
                MetadataApiVersion: server.MetadataApiVersion,
                MetadataSchemaVersion: server.MetadataSchemaVersion,
                DeploymentEnvironment: server.DeploymentEnvironment,
                ObservedAt: envelope.IssuedAt == default ? DateTimeOffset.UtcNow : envelope.IssuedAt));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException)
        {
            return OperateSectionResult<ServerVersionInfo>.Denied(
                OperateSectionStatus.Unavailable,
                "The connected server's capability manifest is unreachable or returned an unreadable response.");
        }
    }

    private static string MapErrorMessage(HttpStatusCode code) => code switch
    {
        HttpStatusCode.NotFound =>
            "The connected server does not expose a capability manifest, so its version cannot be detected.",
        HttpStatusCode.NotImplemented =>
            "The connected server does not advertise a capability manifest.",
        _ => $"The connected server's capability manifest returned {(int)code}.",
    };

    private static OperateSectionStatus MapStatus(HttpStatusCode code) => code switch
    {
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => OperateSectionStatus.Forbidden,
        HttpStatusCode.NotFound => OperateSectionStatus.Missing,
        HttpStatusCode.NotImplemented => OperateSectionStatus.Unsupported,
        _ => OperateSectionStatus.Unavailable,
    };

    private static Uri BuildUri(Uri baseUri, string relativePath) =>
        ConsoleServerHttp.BuildUri(baseUri, relativePath);
}
