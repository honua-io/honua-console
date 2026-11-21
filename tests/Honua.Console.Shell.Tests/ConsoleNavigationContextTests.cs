using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Tests;

public sealed class ConsoleNavigationContextTests
{
    [Theory]
    [InlineData("environments", "Environments")]
    [InlineData("support", "Support")]
    [InlineData("release-notes", "Release notes")]
    [InlineData("operate/health", "Health")]
    [InlineData("operate/jobs/job-abc", "job-abc")]
    public void PageContextLabelNamesTopLevelAreasAndKeepsIdentifiers(string relativePath, string expected) =>
        Assert.Equal(expected, ConsoleNavigation.ContextForPath(relativePath, ConsoleNavigation.Groups).Label);
}
