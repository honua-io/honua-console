using System.Globalization;
using System.Text;

namespace Honua.Console.Shell.Models;

public static class OperateResourceIdentity
{
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
