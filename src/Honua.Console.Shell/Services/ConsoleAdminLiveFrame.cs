using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;

namespace Honua.Console.Shell.Services;

internal sealed record ConsoleAdminLiveFrame(string Kind, long? Position, string Data)
{
    internal static long ParsePosition(string value)
    {
        if (!value.StartsWith("admin:", StringComparison.Ordinal)
            || !long.TryParse(value.AsSpan(6), NumberStyles.None, CultureInfo.InvariantCulture, out var position))
            throw new InvalidDataException("Invalid admin event cursor.");
        return position;
    }

    internal static async IAsyncEnumerable<ConsoleAdminLiveFrame> ReadAsync(
        Stream stream, [EnumeratorCancellation] CancellationToken token)
    {
        using var reader = new StreamReader(stream, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        var data = new StringBuilder();
        var kind = "message";
        long? position = null;
        await foreach (var line in ReadLinesAsync(reader, token).ConfigureAwait(false))
        {
            if (line.Length > 262144 || data.Length + line.Length > 1048576)
                throw new InvalidDataException("Admin event exceeds the supported size.");
            if (line.Length == 0)
            {
                if (data.Length > 0)
                    yield return new ConsoleAdminLiveFrame(kind, position, data.ToString(0, data.Length - 1));
                data.Clear();
                kind = "message";
                position = null;
                continue;
            }
            if (line[0] == ':') continue;
            var colon = line.IndexOf(':');
            var name = colon < 0 ? line : line[..colon];
            var value = colon < 0 ? "" : line[(colon + 1)..];
            if (value.StartsWith(' ')) value = value[1..];
            switch (name)
            {
                case "event": kind = value; break;
                case "id": position = ParsePosition(value); break;
                case "data": data.Append(value).Append('\n'); break;
            }
        }
    }

    private static async IAsyncEnumerable<string> ReadLinesAsync(StreamReader reader,
        [EnumeratorCancellation] CancellationToken token)
    {
        var buffer = new char[4096];
        var line = new StringBuilder();
        var afterCarriageReturn = false;
        while (true)
        {
            // Bound allocation before appending, even when a peer never sends a line ending.
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(TimeSpan.FromSeconds(45));
            var count = await reader.ReadAsync(buffer.AsMemory(), deadline.Token).ConfigureAwait(false);
            if (count == 0) yield break; // An incomplete frame at EOF is never dispatched.
            for (var index = 0; index < count; index++)
            {
                var value = buffer[index];
                if (afterCarriageReturn && value == '\n')
                {
                    afterCarriageReturn = false;
                    continue;
                }
                afterCarriageReturn = value == '\r';
                if (value is '\r' or '\n')
                {
                    yield return line.ToString();
                    line.Clear();
                }
                else
                {
                    if (line.Length >= 262144)
                        throw new InvalidDataException("Admin event line exceeds the supported size.");
                    line.Append(value);
                }
            }
        }
    }

}
