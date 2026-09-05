using Honua.Console.Shell.Services;

namespace Honua.Console.Native.Core.Tests;

public sealed class ConsoleProductModeTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("full")]
    [InlineData("unexpected")]
    public void NormalImageDefaultsToFullFocusedConsole(string? configuredValue)
    {
        Assert.Equal(ConsoleProductMode.Full, ConsoleProductModeParser.Parse(configuredValue));
    }

    [Theory]
    [InlineData("witness")]
    [InlineData(" WITNESS ")]
    public void WitnessModeIsAnExplicitOptIn(string configuredValue)
    {
        var mode = new ConfiguredConsoleProductMode(ConsoleProductModeParser.Parse(configuredValue));

        Assert.True(mode.IsWitness);
        Assert.True(mode.ShowsArea("catalog"));
        Assert.True(mode.ShowsArea("operate"));
        Assert.False(mode.ShowsArea("studio"));
        Assert.False(mode.ShowsArea("share"));
    }

    [Fact]
    public void FullModeKeepsStudioAndGpContainingOperateAreaAvailable()
    {
        var mode = new ConfiguredConsoleProductMode(ConsoleProductMode.Full);

        Assert.False(mode.IsWitness);
        Assert.True(mode.ShowsArea("studio"));
        Assert.True(mode.ShowsArea("operate"));
    }
}
