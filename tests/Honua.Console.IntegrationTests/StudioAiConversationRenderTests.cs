using Bunit;
using Honua.Console.Shell.Components;
using Honua.Console.Shell.Models;
using Microsoft.AspNetCore.Components;

namespace Honua.Console.IntegrationTests;

/// <summary>
/// Docker-free render coverage for the shared <see cref="StudioAiConversation"/> pane. Asserts the
/// design-handoff landmarks (screens-studio.jsx StudioMapAI): the two-column pane with a conversation
/// column (You / Honua turns, structured clarification cards, refine textarea + Send) and the live
/// package-preview slot on the right. The component forwards prompt/answer events and never
/// fabricates package state.
/// </summary>
public sealed class StudioAiConversationRenderTests
{
    private static IReadOnlyList<StudioConversationTurn> SampleTurns() =>
    [
        new StudioConversationTurn(StudioConversationRole.You, "Show parcels coloured by use code.", "2 min ago"),
        new StudioConversationTurn(
            StudioConversationRole.Honua,
            "Map package ready for preview.",
            "just now",
            StudioConversationTone.Ready,
            EvidenceLabel: "view evidence"),
    ];

    private static RenderFragment Preview(string text) => builder =>
    {
        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "data-test", "preview-slot");
        builder.AddContent(2, text);
        builder.CloseElement();
    };

    [Fact]
    public void StudioAiConversation_RendersTwoColumnTurnsAndPreviewSlot()
    {
        using var ctx = new Bunit.TestContext();

        var cut = ctx.RenderComponent<StudioAiConversation>(parameters => parameters
            .Add(p => p.ConversationTitle, "Map from prompt")
            .Add(p => p.Turns, SampleTurns())
            .Add(p => p.PreviewContent, Preview("MapPreview slot")));

        // Two-column pane with the conversation column and the preview column.
        Assert.Contains("studio-ai-pane", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("studio-ai-conversation", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("studio-ai-preview", cut.Markup, StringComparison.Ordinal);

        // Both turn roles render with their authors.
        Assert.Equal(2, cut.FindAll(".studio-ai-turn").Count);
        Assert.Contains("studio-ai-turn-you", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("studio-ai-turn-honua", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("studio-ai-turn-tone-ready", cut.Markup, StringComparison.Ordinal);

        // The right column hosts the surface-supplied preview slot.
        Assert.Contains("MapPreview slot", cut.Find("[data-test=\"preview-slot\"]").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void StudioAiConversation_RendersClarificationCardsAndRaisesAnswer()
    {
        using var ctx = new Bunit.TestContext();

        (StudioConversationClarification Question, StudioConversationChoice Choice)? answered = null;
        var cut = ctx.RenderComponent<StudioAiConversation>(parameters => parameters
            .Add(p => p.Clarifications,
            [
                new StudioConversationClarification(
                    "owner-field",
                    "Owner field?",
                    "owner_name is flagged PII. Include in popup?",
                    [
                        new StudioConversationChoice("include", "include"),
                        new StudioConversationChoice("redact", "redact (recommended)", "drops owner_name from the popup"),
                    ]),
            ])
            .Add(p => p.OnAnswer, value => answered = value));

        Assert.Contains("studio-ai-clarification-card", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Owner field?", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("flagged PII", cut.Markup, StringComparison.Ordinal);
        Assert.Equal("1 open", cut.Find(".studio-ai-open-count").TextContent.Trim());

        var choices = cut.FindAll(".studio-ai-clarification-choice");
        Assert.Equal(2, choices.Count);
        choices[1].Click();

        Assert.NotNull(answered);
        Assert.Equal("owner-field", answered!.Value.Question.Id);
        Assert.Equal("redact", answered.Value.Choice.Id);
    }

    [Fact]
    public void StudioAiConversation_Send_RaisesOnSendWithRefineText()
    {
        using var ctx = new Bunit.TestContext();

        string? sent = null;
        var cut = ctx.RenderComponent<StudioAiConversation>(parameters => parameters
            .Add(p => p.Turns, SampleTurns())
            .Add(p => p.OnSend, text => sent = text));

        // Send is disabled until the author types into the refine textarea.
        Assert.True(cut.Find(".studio-ai-send").HasAttribute("disabled"));

        cut.Find(".studio-ai-refine-input").Input("Categorical, and redact owner.");
        Assert.False(cut.Find(".studio-ai-send").HasAttribute("disabled"));

        cut.Find(".studio-ai-send").Click();
        Assert.Equal("Categorical, and redact owner.", sent);

        // The textarea clears after dispatch and Send disables again.
        Assert.True(cut.Find(".studio-ai-send").HasAttribute("disabled"));
    }

    [Fact]
    public void StudioAiConversation_WhenBusy_DisablesRefineAndClarificationChoices()
    {
        using var ctx = new Bunit.TestContext();

        var cut = ctx.RenderComponent<StudioAiConversation>(parameters => parameters
            .Add(p => p.Busy, true)
            .Add(p => p.Clarifications,
            [
                new StudioConversationClarification(
                    "palette",
                    "Use-code colours",
                    "12 distinct codes.",
                    [new StudioConversationChoice("categorical", "categorical")]),
            ]));

        Assert.True(cut.Find(".studio-ai-refine-input").HasAttribute("disabled"));
        Assert.True(cut.Find(".studio-ai-send").HasAttribute("disabled"));
        Assert.True(cut.Find(".studio-ai-clarification-choice").HasAttribute("disabled"));
    }
}
