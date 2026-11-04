using System.Net.Http;

namespace Honua.Console.Shell.Services;

// Privileged clients fail closed when no operator is resolved. Public clients allow an anonymous caller and forward an operator bearer when one exists.
public interface IHonuaServerBoundClientFactory
{
    HttpClient CreateServerBoundClient(Uri baseUri, TimeSpan? timeout = null);

    HttpClient CreateManagerBoundClient(Uri baseUri, TimeSpan? timeout = null) => CreateServerBoundClient(baseUri, timeout);

    HttpClient CreatePublicClient(Uri baseUri, TimeSpan? timeout = null);
}
