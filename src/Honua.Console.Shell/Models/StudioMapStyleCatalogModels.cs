namespace Honua.Console.Shell.Models;

public sealed record StudioMapStyleOption(string StyleId, string? Title)
{
    public string Label => string.IsNullOrWhiteSpace(Title) ? StyleId : Title!;
}

public sealed record StudioMapStyleCatalog(
    IReadOnlyList<StudioMapStyleOption> Styles,
    string? DefaultStyleId,
    StudioMapCapabilityState? Issue)
{
    public static StudioMapStyleCatalog Empty { get; } = new([], null, null);

    public bool HasStyles => Styles.Count > 0;

    public bool IsKnown(string? styleId) =>
        !string.IsNullOrWhiteSpace(styleId)
        && Styles.Any(option => string.Equals(option.StyleId, styleId, StringComparison.OrdinalIgnoreCase));
}

public sealed record StudioMapCapabilityState(
    string Surface,
    string State,
    string Contract,
    string Detail,
    string? Summary = null,
    int? StatusCode = null,
    string? IssueRef = null);
