using Microsoft.AspNetCore.Components;

namespace Honua.Console.Shell.Models;

public sealed record PublishWizardStep(
    string Label,
    RenderFragment Body,
    bool? IsValid = null,
    string? ContinueLabel = null);
