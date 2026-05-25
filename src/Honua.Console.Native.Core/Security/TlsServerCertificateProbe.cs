using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using Honua.Console.Shell.Models;

namespace Honua.Console.Native.Core.Security;

/// <summary>
/// Observes the server TLS certificate by completing (or attempting) a TLS handshake against the
/// profile's <see cref="ConsoleEnvironmentProfile.ServerBaseUri"/>. The presented server certificate
/// is captured before any application data flows; the handshake result itself is not trusted here -
/// trust decisions are made by <see cref="ConsoleTrustEvaluator"/> against the pinned fingerprint.
/// </summary>
public sealed class TlsServerCertificateProbe : IConsoleServerCertificateProbe
{
    private static readonly TimeSpan HandshakeTimeout = TimeSpan.FromSeconds(10);

    public async Task<string?> ObserveServerFingerprintAsync(
        ConsoleEnvironmentProfile profile,
        X509Certificate2? clientCertificate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var uri = profile.ServerBaseUri;
        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var port = uri.Port > 0 ? uri.Port : 443;
        string? fingerprint = null;

        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            linked.CancelAfter(HandshakeTimeout);

            using var tcp = new TcpClient();
            await tcp.ConnectAsync(uri.Host, port, linked.Token).ConfigureAwait(false);

            await using var ssl = new SslStream(
                tcp.GetStream(),
                leaveInnerStreamOpen: false,
                (_, certificate, _, _) =>
                {
                    if (certificate is not null)
                    {
                        fingerprint = NativeServerTrust.ComputeSha256Fingerprint(certificate);
                    }

                    return true;
                });

            var options = new SslClientAuthenticationOptions { TargetHost = uri.Host };
            if (clientCertificate is not null)
            {
                options.ClientCertificates = [clientCertificate];
            }

            await ssl.AuthenticateAsClientAsync(options, linked.Token).ConfigureAwait(false);
        }
        catch (Exception) when (fingerprint is not null)
        {
            // Server certificate was observed before the handshake failed (e.g. server demanded a
            // client certificate we did not present). The observed fingerprint is still valid to pin.
        }
        catch (Exception)
        {
            return null;
        }

        return fingerprint;
    }
}
