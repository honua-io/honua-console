using Honua.Console.Shell.Security;
using Xunit;

namespace Honua.Console.Shell.Tests;

public sealed class ConsoleSafeUrlTests
{
    [Theory]
    [InlineData("https://example.test/pr/42", "https://example.test/pr/42")]
    [InlineData("http://localhost:8080/items/1", "http://localhost:8080/items/1")]
    [InlineData("  https://example.test/path?a=1#frag  ", "https://example.test/path?a=1#frag")]
    public void NormalizeHttpUrl_AllowsSafeHttpAndHttpsUrls(string raw, string expected)
    {
        Assert.Equal(expected, ConsoleSafeUrl.NormalizeHttpUrl(raw));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("javascript:alert(1)")]
    [InlineData("JAVASCRIPT:alert(1)")]
    [InlineData("data:text/html,<script>alert(1)</script>")]
    [InlineData("vbscript:msgbox(1)")]
    [InlineData("file:///etc/passwd")]
    [InlineData("https://user:pass@example.test/path")]
    [InlineData("https://user@example.test/path")]
    [InlineData("//evil.example/path")]
    [InlineData("/operate/releases")]
    [InlineData("https://example.test/a\nb")]
    [InlineData("https://example.test/a\\b")]
    public void NormalizeHttpUrl_RejectsUnsafeOrRelativeUrlsByDefault(string? raw)
    {
        Assert.Null(ConsoleSafeUrl.NormalizeHttpUrl(raw));
    }

    [Theory]
    [InlineData("/rest/services/cadastre/FeatureServer/7")]
    [InlineData("/catalog")]
    [InlineData("/operate/releases/123?tab=diff")]
    [InlineData("https://example.test/maps/1")]
    public void NormalizeHttpOrRelativeUrl_AcceptsSafeRootRelativeAndHttpUrls(string raw)
    {
        Assert.Equal(raw, ConsoleSafeUrl.NormalizeHttpOrRelativeUrl(raw));
    }

    [Theory]
    [InlineData("//evil.example/path")]
    [InlineData("/\\evil.example/path")]
    [InlineData("/path\\with\\backslash")]
    [InlineData("/http://evil.example")]
    [InlineData("/\t/evil.example")]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,boom")]
    public void NormalizeHttpOrRelativeUrl_RejectsSchemeAndProtocolRelativeBypass(string raw)
    {
        Assert.Null(ConsoleSafeUrl.NormalizeHttpOrRelativeUrl(raw));
    }
}
