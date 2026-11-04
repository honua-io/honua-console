using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;

namespace Honua.Console.Web.Auth;

// Trust X-Forwarded-Proto and X-Forwarded-Host from private peers as well as loopback, because the image listens on HTTP and a public remote stays untrusted.
public static class ConsoleForwardedHeaders
{
    public static void Apply(ForwardedHeadersOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.ForwardedHeaders = ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
        options.KnownIPNetworks.Add(new System.Net.IPNetwork(IPAddress.Parse("10.0.0.0"), 8));
        options.KnownIPNetworks.Add(new System.Net.IPNetwork(IPAddress.Parse("172.16.0.0"), 12));
        options.KnownIPNetworks.Add(new System.Net.IPNetwork(IPAddress.Parse("192.168.0.0"), 16));
        options.KnownIPNetworks.Add(new System.Net.IPNetwork(IPAddress.Parse("fc00::"), 7));
        options.KnownIPNetworks.Add(new System.Net.IPNetwork(IPAddress.Parse("fe80::"), 10));
    }

    internal static async Task<(string Scheme, string Host)> InvokeAsync(
        string remoteIp,
        string scheme,
        string host,
        string? forwardedProto,
        string? forwardedHost)
    {
        var options = new ForwardedHeadersOptions();
        Apply(options);
        var middleware = new ForwardedHeadersMiddleware(
            _ => Task.CompletedTask,
            Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance,
            Options.Create(options));
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(remoteIp);
        context.Request.Scheme = scheme;
        context.Request.Host = new HostString(host);
        if (forwardedProto is not null)
        {
            context.Request.Headers["X-Forwarded-Proto"] = forwardedProto;
        }

        if (forwardedHost is not null)
        {
            context.Request.Headers["X-Forwarded-Host"] = forwardedHost;
        }

        await middleware.Invoke(context).ConfigureAwait(false);
        return (context.Request.Scheme, context.Request.Host.Host);
    }
}
