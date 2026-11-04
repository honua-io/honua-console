namespace Honua.Console.Shell.Services;

// Selecting a product mode does not change server authorization.
public enum ConsoleProductMode
{
    Full,
    Witness,
}

public interface IConsoleProductMode
{
    ConsoleProductMode Mode { get; }

    bool IsWitness { get; }

    bool ShowsArea(string areaId);
}

public sealed class ConfiguredConsoleProductMode(ConsoleProductMode mode) : IConsoleProductMode
{
    private static readonly HashSet<string> WitnessAreas = new(StringComparer.OrdinalIgnoreCase)
    {
        "catalog",
        "operate",
    };

    public ConsoleProductMode Mode { get; } = mode;

    public bool IsWitness => Mode == ConsoleProductMode.Witness;

    public bool ShowsArea(string areaId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(areaId);
        return !IsWitness || WitnessAreas.Contains(areaId);
    }
}

public static class ConsoleProductModeParser
{
    public static ConsoleProductMode Parse(string? configuredValue) =>
        string.Equals(configuredValue?.Trim(), "witness", StringComparison.OrdinalIgnoreCase)
            ? ConsoleProductMode.Witness
            : ConsoleProductMode.Full;
}
