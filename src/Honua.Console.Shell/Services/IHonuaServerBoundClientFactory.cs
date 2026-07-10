using System.Net.Http;

namespace Honua.Console.Shell.Services;

/// <summary>
/// The seam the Family-A server-bound client registrations use to obtain their <see cref="HttpClient"/>
/// (honua-console#254). It lets the multi-operator browser Web host supply
/// <see cref="System.Net.Http.IHttpClientFactory"/>-managed clients whose handler chain injects the
/// operator binding/auth per request/circuit and FAILS CLOSED for an unresolved operator on the
/// privileged path — without rewriting each typed client — while the native single-operator host and the
/// host-independent tests fall back to the self-contained pooled client
/// <see cref="HonuaServerClientFactory"/> builds directly.
///
/// The split exists because the server-bound surface is NOT uniform:
/// <list type="bullet">
/// <item><b>Privileged</b> clients (catalog/content authoring, share, RBAC, Studio package/lifecycle,
/// admin operate, temporal, version management, …) act with honua-server privileges and MUST refuse an
/// unresolved operator rather than silently fall back to the shared admin key / shared anonymous
/// partition — the recurring fail-open class this issue closes. They use
/// <see cref="CreateServerBoundClient"/>.</item>
/// <item><b>Legitimately-anonymous-capable</b> clients — the <c>/public</c> open-data catalog reads and
/// the public OGC API <c>/ogc/styles</c> list — must keep rendering for anonymous visitors by explicit
/// design. They use <see cref="CreatePublicClient"/>, which forwards the operator bearer WHEN one is
/// resolved but tolerates an anonymous caller (the documented admin-key / anonymous fallback).</item>
/// </list>
/// </summary>
public interface IHonuaServerBoundClientFactory
{
    /// <summary>
    /// Builds a client for a PRIVILEGED server-bound surface. Its handler chain fails closed when no
    /// operator is resolved (no <c>__anonymous__</c> fallback), so an unresolved operator can never act
    /// with honua-server privileges.
    /// </summary>
    HttpClient CreateServerBoundClient(Uri baseUri, TimeSpan? timeout = null);

    /// <summary>
    /// Builds a client for a legitimately-ANONYMOUS-capable surface (the <c>/public</c> open-data catalog
    /// reads and the public OGC <c>/ogc/styles</c> list). Its handler chain forwards the operator's bearer
    /// when one is resolved but, by design, tolerates an anonymous caller rather than failing closed.
    /// </summary>
    HttpClient CreatePublicClient(Uri baseUri, TimeSpan? timeout = null);
}
