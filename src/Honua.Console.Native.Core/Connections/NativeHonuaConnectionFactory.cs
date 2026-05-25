using System.Net.Http.Headers;
using System.Security.Cryptography.X509Certificates;
using Grpc.Net.Client;
using Honua.Console.Native.Core.Security;
using Honua.Console.Shell.Models;

namespace Honua.Console.Native.Core.Connections;

public sealed class NativeHonuaConnectionFactory
{
    private readonly IConsoleAccountTokenProvider _tokenProvider;
    private readonly IClientCertificateResolver _certificateResolver;

    public NativeHonuaConnectionFactory(
        IConsoleAccountTokenProvider tokenProvider,
        IClientCertificateResolver certificateResolver)
    {
        _tokenProvider = tokenProvider;
        _certificateResolver = certificateResolver;
    }

    public Task<NativeHonuaConnection> CreateAsync(
        ConsoleEnvironmentProfile profile,
        CancellationToken cancellationToken = default) =>
        CreateAsync(profile, trustedServerFingerprint: null, cancellationToken);

    public async Task<NativeHonuaConnection> CreateAsync(
        ConsoleEnvironmentProfile profile,
        string? trustedServerFingerprint,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        cancellationToken.ThrowIfCancellationRequested();

        var certificate = await _certificateResolver.ResolveAsync(profile, cancellationToken).ConfigureAwait(false);
        try
        {
            return CreateWithCertificate(profile, certificate, trustedServerFingerprint, await _tokenProvider
                .GetAccessTokenAsync(profile, cancellationToken).ConfigureAwait(false));
        }
        catch
        {
            certificate?.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Builds a native connection that attaches exactly <paramref name="clientCertificate"/> - the
    /// certificate the trust gate already resolved and validated - so the transport never presents a
    /// different certificate than the one validated (no resolve-time-of-check/use gap). The returned
    /// connection takes ownership of the certificate and disposes it.
    /// </summary>
    public async Task<NativeHonuaConnection> CreateAsync(
        ConsoleEnvironmentProfile profile,
        X509Certificate2? clientCertificate,
        string? trustedServerFingerprint,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        cancellationToken.ThrowIfCancellationRequested();

        var accessToken = await _tokenProvider.GetAccessTokenAsync(profile, cancellationToken).ConfigureAwait(false);
        return CreateWithCertificate(profile, clientCertificate, trustedServerFingerprint, accessToken);
    }

    private static NativeHonuaConnection CreateWithCertificate(
        ConsoleEnvironmentProfile profile,
        X509Certificate2? clientCertificate,
        string? trustedServerFingerprint,
        string? accessToken)
    {
        var handler = new HttpClientHandler();
        if (!string.IsNullOrWhiteSpace(trustedServerFingerprint))
        {
            var serverValidation = NativeServerTrust.CreateServerValidationCallback(trustedServerFingerprint);
            handler.ServerCertificateCustomValidationCallback =
                (message, certificate, chain, errors) => serverValidation(message, certificate, chain, errors);
        }

        // Only a certificate with a usable private key can complete client authentication. A
        // public-only certificate would silently disable mTLS while appearing attached, so drop it
        // here as well. The trust gate already blocks this upstream; this is defense in depth that
        // also covers the resolve-internally overloads.
        if (clientCertificate is not null && !clientCertificate.HasPrivateKey)
        {
            clientCertificate.Dispose();
            clientCertificate = null;
        }

        if (clientCertificate is not null)
        {
            handler.ClientCertificates.Add(clientCertificate);
        }

        var httpClient = new HttpClient(handler, disposeHandler: true)
        {
            BaseAddress = profile.ServerBaseUri
        };

        if (!string.IsNullOrWhiteSpace(accessToken))
        {
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        var channel = GrpcChannel.ForAddress(
            profile.ServerBaseUri,
            new GrpcChannelOptions
            {
                HttpClient = httpClient,
                DisposeHttpClient = false
            });

        return new NativeHonuaConnection(profile, httpClient, channel, accessToken, clientCertificate);
    }
}

public sealed class NativeHonuaConnection : IAsyncDisposable, IDisposable
{
    public NativeHonuaConnection(
        ConsoleEnvironmentProfile profile,
        HttpClient httpClient,
        GrpcChannel grpcChannel,
        string? bearerToken,
        X509Certificate2? clientCertificate)
    {
        Profile = profile;
        HttpClient = httpClient;
        GrpcChannel = grpcChannel;
        BearerToken = bearerToken;
        ClientCertificate = clientCertificate;
    }

    public ConsoleEnvironmentProfile Profile { get; }

    public HttpClient HttpClient { get; }

    public GrpcChannel GrpcChannel { get; }

    public string? BearerToken { get; }

    public X509Certificate2? ClientCertificate { get; }

    public void Dispose()
    {
        GrpcChannel.Dispose();
        HttpClient.Dispose();
        ClientCertificate?.Dispose();
    }

    public ValueTask DisposeAsync()
    {
        GrpcChannel.Dispose();
        HttpClient.Dispose();
        ClientCertificate?.Dispose();
        return ValueTask.CompletedTask;
    }
}
