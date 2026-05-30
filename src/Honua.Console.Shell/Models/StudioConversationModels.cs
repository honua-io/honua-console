using System.Collections.Generic;

namespace Honua.Console.Shell.Models;

/// <summary>
/// Who authored a turn in the shared Studio AI conversation pane.
/// </summary>
public enum StudioConversationRole
{
    /// <summary>An author-side message.</summary>
    You,

    /// <summary>An assistant-side message from Honua.</summary>
    Honua,
}

/// <summary>
/// The visual tone of a Honua turn, mirroring the design-handoff conversation bubbles in
/// screens-studio.jsx: a neutral note, a clarification prompt (accent), or a ready/success state.
/// </summary>
public enum StudioConversationTone
{
    Neutral,
    Clarification,
    Ready,
}

/// <summary>
/// One conversation turn (You / Honua) in the shared Studio AI conversation pane.
/// </summary>
/// <param name="Role">Who authored the turn.</param>
/// <param name="Body">The turn text.</param>
/// <param name="Timestamp">Relative time label (e.g. "2 min ago", "just now").</param>
/// <param name="Tone">Visual tone for Honua turns; ignored for author turns.</param>
/// <param name="EvidenceLabel">Optional evidence link label rendered after the body.</param>
public sealed record StudioConversationTurn(
    StudioConversationRole Role,
    string Body,
    string? Timestamp = null,
    StudioConversationTone Tone = StudioConversationTone.Neutral,
    string? EvidenceLabel = null);

/// <summary>
/// One choice within a structured clarification card.
/// </summary>
/// <param name="Id">Stable choice id passed back to the answer callback.</param>
/// <param name="Label">Choice label.</param>
/// <param name="Effect">Optional secondary text describing what the choice does.</param>
public sealed record StudioConversationChoice(string Id, string Label, string? Effect = null);

/// <summary>
/// A structured clarification card asking the author a question with discrete choices.
/// Mirrors the clarification cards in screens-studio.jsx and the StudioPage clarification panel.
/// </summary>
/// <param name="Id">Stable question id passed back to the answer callback.</param>
/// <param name="Label">The question.</param>
/// <param name="Reason">Why Honua is asking (the muted sub-text).</param>
/// <param name="Choices">The discrete answer choices.</param>
public sealed record StudioConversationClarification(
    string Id,
    string Label,
    string Reason,
    IReadOnlyList<StudioConversationChoice> Choices);
