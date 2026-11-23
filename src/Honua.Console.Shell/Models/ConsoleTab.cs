namespace Honua.Console.Shell.Models;

public sealed record ConsoleTab(string Id, string Label, string PanelId)
{
    // Optional item count shown after the label (v4: "Layers 4"); hidden from the accessible name, so pages repeat it in text.
    public int? Count { get; init; }
}
