using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;

namespace Honua.Console.Native.Core.Security;

/// <summary>
/// Thin HttpClient binding to the closed honua-server#1171 client-certificate validation endpoint,
/// kept behind the <see cref="Honua.Console.Contracts"/> shim shapes until the SDK projects this
/// client (honua-console#7). Sends the certificate's public PEM in the request body; never transmits
/// or stores private-key material.
/// </summary>
public sealed class ServerClientCertificateValidationClient : IConsoleClientCertificateValidationClient
{
    private const string ValidatePath = "/api/v1/admin/security/client-certificates/validate";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IConsoleAccountTokenProvider _tokenProvider;

    public ServerClientCertificateValidationClient(IConsoleAccountTokenProvider tokenProvider)
    {
        _tokenProvider = tokenProvider;
    }

    public async Task<ConsoleClientCertificateValidationResult> ValidateAsync(
        ConsoleEnvironmentProfile profile,
        X509Certificate2 clientCertificate,
        string? trustedServerFingerprint = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(clientCertificate);

        var handler = new SocketsHttpHandler();
        handler.SslOptions.RemoteCertificateValidationCallback =
            NativeServerTrust.CreateServerValidationCallback(trustedServerFingerprint);
        handler.SslOptions.ClientCertificates = [clientCertificate];

        using var httpClient = new HttpClient(handler, disposeHandler: true)
        {
            BaseAddress = profile.ServerBaseUri
        };

        var accessToken = await _tokenProvider.GetAccessTokenAsync(profile, cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(accessToken))
        {
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        var request = new ConsoleClientCertificateValidationRequest
        {
            Certificate = clientCertificate.ExportCertificatePem(),
            Encoding = "pem",
            ProfileId = string.IsNullOrWhiteSpace(profile.ClientCertificate.TrustProfileId)
                ? null
                : profile.ClientCertificate.TrustProfileId
        };

        var validateUri = new Uri(profile.ServerBaseUri, ValidatePath);
        using var response = await httpClient.PostAsJsonAsync(validateUri, request, JsonOptions, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var envelope = await response.Content
            .ReadFromJsonAsync<ConsoleServerEnvelope<ConsoleClientCertificateValidationResult>>(JsonOptions, cancellationToken)
            .ConfigureAwait(false);

        return envelope?.Data ?? new ConsoleClientCertificateValidationResult
        {
            Valid = false,
            Code = ConsoleCertificateValidationCodes.Missing,
            Detail = "The server returned an empty validation response."
        };
    }
}
