using Microsoft.AspNetCore.Components;

namespace Honua.Console.Shell.Models;

/// <summary>
/// One step in the shared <c>PublishWizard</c> shell. Mirrors the design-handoff Stepper steps in
/// screens-quick-publish.jsx (Service → Layer → Review) and screens-publish-flow.jsx
/// (Target → Compatibility → Slot → Fields → Projection → Access → Review). A surface supplies the
/// labelled step, its body content, and an optional per-step validity predicate that gates the
/// forward navigation.
/// </summary>
/// <param name="Label">The step label shown in the stepper.</param>
/// <param name="Body">The step body rendered when the step is current.</param>
/// <param name="IsValid">
/// Whether the step is complete enough to advance. Null means "always allow forward" (e.g. an
/// informational step). When false, the Continue control is disabled.
/// </param>
/// <param name="ContinueLabel">
/// Optional override for the forward-nav label (the mockups use "Continue · Layer →" etc.).
/// </param>
public sealed record PublishWizardStep(
    string Label,
    RenderFragment Body,
    bool? IsValid = null,
    string? ContinueLabel = null);
