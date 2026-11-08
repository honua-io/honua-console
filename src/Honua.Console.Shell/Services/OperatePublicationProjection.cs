using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

internal sealed class OperatePublicationProjection(Uri serverBaseUri)
{
    public IReadOnlyList<OperatePublicationSlot> ForService(string serviceName, IReadOnlyList<string> protocols) =>
        protocols.Select(protocol => new OperatePublicationSlot(
            protocol, ProtocolUrl(serviceName, protocol), "Exposed by honua-server")).ToArray();

    internal string? ProtocolUrl(string serviceName, string protocol) =>
        HonuaServedRoutes.PathFor(serviceName, protocol) is { } path
            ? serverBaseUri.ToString().TrimEnd('/') + path : null;
}
