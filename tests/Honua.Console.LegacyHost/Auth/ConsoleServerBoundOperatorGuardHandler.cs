using System.Net.Http;

namespace Honua.Console.Web.Auth;

// The handler pool outlives the circuit, so this reads the ambient operator context instead of a captured scope, and an unresolved operator is refused before a credential is attached.
public sealed class ConsoleServerBoundOperatorGuardHandler : DelegatingHandler
{
    private readonly IConsoleOperatorContext _operatorContext;

    public ConsoleServerBoundOperatorGuardHandler(IConsoleOperatorContext operatorContext)
    {
        _operatorContext = operatorContext ?? throw new ArgumentNullException(nameof(operatorContext));
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        // Hard deny: throws ConsoleOperatorContextUnresolvedException when no authenticated operator is in
        // scope, so the inner binding handler (retarget + bearer/admin-key) is never reached.
        _ = _operatorContext.RequireOperatorKey();
        return base.SendAsync(request, cancellationToken);
    }
}
