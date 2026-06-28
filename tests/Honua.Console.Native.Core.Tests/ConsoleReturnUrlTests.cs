using Honua.Console.Shell.Security;
using Xunit;

namespace Honua.Console.Native.Core.Tests;

/// <summary>
/// Guards the shared returnTo open-redirect sanitiser. The regression of record is the
/// slash+backslash bypass (<c>/\evil.com</c>): it passes a naive "starts with / but not //"
/// check, but browsers normalise <c>\</c> to <c>/</c>, yielding the protocol-relative external
/// host <c>//evil.com</c>.
/// </summary>
public sealed class ConsoleReturnUrlTests
{
    [Theory]
    [InlineData("/operate")]
    [InlineData("/catalog/layers?id=5")]
    [InlineData("/")]
    public void Sanitize_AllowsSiteRelativePaths(string value)
    {
        Assert.Equal(value, ConsoleReturnUrl.Sanitize(value));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("//evil.com")]
    [InlineData("https://evil.com")]
    [InlineData("http://evil.com")]
    [InlineData("/\\evil.com")]      // slash + backslash -> //evil.com after browser normalisation
    [InlineData("/\\/evil.com")]
    [InlineData("\\\\evil.com")]
    [InlineData("/path\\with\\backslash")]
    [InlineData("javascript:alert(1)")]
    public void Sanitize_RejectsNonSiteRelativeOrBypass(string? value)
    {
        Assert.Equal("/", ConsoleReturnUrl.Sanitize(value));
    }
}
