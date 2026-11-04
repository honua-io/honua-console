using System.Text;
using Honua.Console.Contracts;

namespace Honua.Console.Shell.Models;

public sealed record SupportSelfServiceContext
{
    public IReadOnlyList<ChatCompletionMessage> Transcript { get; init; } = [];

    public IReadOnlyList<SupportKbRecord> ViewedKb { get; init; } = [];

    public int AssistantTurns { get; init; }

    public bool HasActivity => Transcript.Count > 0 || ViewedKb.Count > 0;

    public string ToSymptomContext()
    {
        if (!HasActivity)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        builder.AppendLine("--- L0 self-service (assistant + KB) ---");

        if (Transcript.Count > 0)
        {
            builder.AppendLine("Assistant transcript:");
            foreach (var message in Transcript)
            {
                var who = string.Equals(message.Role, "assistant", StringComparison.OrdinalIgnoreCase)
                    ? "Assistant"
                    : "User";
                builder.AppendLine($"  {who}: {message.Content.Trim()}");
            }
        }

        if (ViewedKb.Count > 0)
        {
            builder.AppendLine($"KB articles viewed ({ViewedKb.Count}):");
            foreach (var record in ViewedKb)
            {
                var code = string.IsNullOrWhiteSpace(record.FaultCode) ? record.Id : record.FaultCode;
                builder.AppendLine($"  - {record.Title} [{code}]");
            }
        }

        return builder.ToString().TrimEnd();
    }
}
