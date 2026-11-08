using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

internal sealed class OperatePublicationProjection(Uri serverBaseUri)
{
    public IReadOnlyList<OperatePublicationSlot> ForService(string? serviceName, IReadOnlyList<string> protocols, string? folderName = null) =>
        protocols.Select(protocol => new OperatePublicationSlot(
            protocol, ProtocolUrl(serviceName, protocol, folderName), "Exposed by honua-server")).ToArray();

    internal string? ProtocolUrl(string? serviceName, string protocol, string? folderName = null) =>
        HonuaServedRoutes.PathFor(serviceName, protocol, folderName) is { } path
            ? serverBaseUri.ToString().TrimEnd('/') + path : null;
}
