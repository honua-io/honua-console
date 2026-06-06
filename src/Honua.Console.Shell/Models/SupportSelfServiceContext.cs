using System.Text;
using Honua.Console.Contracts;

namespace Honua.Console.Shell.Models;

/// <summary>
/// The L0 self-service activity carried out of <c>SupportSelfServicePanel</c>:
/// the assistant transcript and the KB items the user opened. When the user
/// still needs help and proceeds to the ticket form, this is folded into the
/// ticket symptoms so support inherits what was already tried (honua-console#165).
/// </summary>
public sealed record SupportSelfServiceContext
{
    public IReadOnlyList<ChatCompletionMessage> Transcript { get; init; } = [];

    public IReadOnlyList<SupportKbRecord> ViewedKb { get; init; } = [];

    /// <summary>Count of user-sent assistant messages (deflection signal).</summary>
    public int AssistantTurns { get; init; }

    public bool HasActivity => Transcript.Count > 0 || ViewedKb.Count > 0;

    /// <summary>
    /// Renders the assistant transcript + viewed KB items as a human-readable
    /// block appended to the ticket symptoms, so the operator (and L1 triage)
    /// see what L0 already covered. Returns empty when there was no activity.
    /// </summary>
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
