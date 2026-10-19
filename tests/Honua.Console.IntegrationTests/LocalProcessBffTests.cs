using Honua.Console.Web;

namespace Honua.Console.IntegrationTests;

public sealed class LocalProcessBffTests
{
    [Theory]
    [InlineData("runs", "GET", true)]
    [InlineData("runs", "POST", true)]
    [InlineData("runs", "PUT", false)]
    [InlineData("project/identity", "GET", true)]
    [InlineData("project/bind", "POST", true)]
    [InlineData("project/bind", "GET", false)]
    [InlineData("project/bindings/workspace", "DELETE", true)]
    [InlineData("project/bindings/workspace", "POST", false)]
    [InlineData("sources/model.py", "GET", true)]
    [InlineData("sources/model.py", "PUT", true)]
    [InlineData("sources/model.py", "POST", false)]
    [InlineData("snapshots", "POST", true)]
    [InlineData("snapshots/snapshot/sources/model.py", "GET", true)]
    [InlineData("snapshots/snapshot/sources/model.py", "PUT", false)]
    [InlineData("commands/command", "GET", true)]
    [InlineData("commands/command", "POST", false)]
    [InlineData("sources/../secrets.py", "GET", false)]
    [InlineData("sources/%2fsecret.py", "GET", false)]
    [InlineData("sources/.env", "GET", false)]
    [InlineData("arbitrary/path", "GET", false)]
    public void CanonicalProcessProxyRestrictsRoutesAndMutations(string path, string method, bool accepted) =>
        Assert.Equal(accepted, LocalProcessBff.AllowedPath().IsMatch(path) && LocalProcessBff.AllowsMethod(path, method));
}
