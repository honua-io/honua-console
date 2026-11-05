using System.Globalization;
using System.Text;
using Honua.Console.Contracts;

namespace Honua.Console.Shell.Models;

public static class OperateResourceIdentity
{
    public static string ForConnection(HonuaAdminConnectionSummary connection) => connection.ConnectionId == Guid.Empty
        ? NormalizeToken(new[] { connection.Name, connection.Host, "connection" }.First(value => !string.IsNullOrWhiteSpace(value))!)
        : connection.ConnectionId.ToString("D");

    public static string ForPublishedLayer(string connectionId, int layerId) =>
        $"{NormalizeToken(connectionId)}-layer-{layerId.ToString(CultureInfo.InvariantCulture)}";

    public static string NormalizeToken(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            if (char.IsAsciiLetterOrDigit(character)) builder.Append(char.ToLowerInvariant(character));
            else if (character is '-' or '_') builder.Append(character);
            else if (char.IsWhiteSpace(character) || character is ':' or '/' or '.') builder.Append('-');
        }

        var normalized = builder.ToString().Trim('-');
        return string.IsNullOrWhiteSpace(normalized) ? "item" : normalized;
    }
}
