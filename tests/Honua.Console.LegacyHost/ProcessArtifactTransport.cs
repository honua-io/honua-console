using Microsoft.AspNetCore.Antiforgery;
using Microsoft.Extensions.Options;

namespace Honua.Console.Web;

internal static partial class AgentWorkspaceBff
{
    internal const int ProcessImportBodyMaximumBytes = 20 * 1024 * 1024;
    // The bounded inline browser path reserves framing for one file and the two
    // allowed small fields; this is a file-byte capability, not the raw body cap.
    internal const int ProcessImportMultipartHeadroomBytes = 64 * 1024;
    internal const int ProcessArtifactFileMaximumBytes = ProcessImportBodyMaximumBytes - ProcessImportMultipartHeadroomBytes;

    private static bool HasProcessImportAntiforgeryHeader(HttpContext context)
    {
        // Do not let antiforgery fall back to parsing an unbounded multipart body
        // in search of its form field before the upload transport can bound it.
        var name = context.RequestServices.GetRequiredService<IOptions<AntiforgeryOptions>>().Value.HeaderName;
        return name is not null && context.Request.Headers.TryGetValue(name, out var token) && token.Count == 1 &&
            token[0] is { Length: > 0 and <= 4096 };
    }
}
