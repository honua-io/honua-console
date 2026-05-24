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

    public async Task<NativeHonuaConnection> CreateAsync(
        ConsoleEnvironmentProfile profile,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        cancellationToken.ThrowIfCancellationRequested();

        var handler = new HttpClientHandler();
        var certificate = await _certificateResolver.ResolveAsync(profile, cancellationToken).ConfigureAwait(false);
        if (certificate is not null)
        {
            handler.ClientCertificates.Add(certificate);
        }

        var httpClient = new HttpClient(handler, disposeHandler: true)
        {
            BaseAddress = profile.ServerBaseUri
        };

        var accessToken = await _tokenProvider.GetAccessTokenAsync(profile, cancellationToken).ConfigureAwait(false);
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

        return new NativeHonuaConnection(profile, httpClient, channel, accessToken, certificate);
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
    }

    public ValueTask DisposeAsync()
    {
        GrpcChannel.Dispose();
        HttpClient.Dispose();
        return ValueTask.CompletedTask;
    }
}
