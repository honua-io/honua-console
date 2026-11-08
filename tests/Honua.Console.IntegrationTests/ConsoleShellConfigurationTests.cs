using Honua.Console.Shell.DependencyInjection;
using Honua.Console.Shell.Models;
using Honua.Console.Web;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

public sealed class ConsoleShellConfigurationTests
{
    [Theory]
    [InlineData("true", null, true)]
    [InlineData("false", null, false)]
    [InlineData(null, "true", true)]
    [InlineData(null, "false", false)]
    [InlineData(null, null, false)]
    [InlineData("invalid", null, false)]
    [InlineData(null, "invalid", false)]
    [InlineData("true", "false", true)]
    [InlineData("false", "true", false)]
    [InlineData("invalid", "true", false)]
    [InlineData("", "true", false)]
    [InlineData(" ", "true", false)]
    public void CatalogFlagBindsToSharedOptions(string? keyValue, string? environmentValue, bool expectedEnabled)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Honua:Console:Catalogs:Enabled"] = keyValue,
            ["HONUA_CONSOLE_CATALOGS_ENABLED"] = environmentValue
        }).Build();

        var shellOptions = ConsoleShellConfiguration.ReadOptions(configuration);

        Assert.Equal(expectedEnabled, shellOptions.CatalogAdminEnabled);
        var services = new ServiceCollection();
        services.AddHonuaConsoleShell(shellOptions);
        using var provider = services.BuildServiceProvider();
        Assert.Equal(expectedEnabled, provider.GetRequiredService<CatalogAdminOptions>().Enabled);
    }
}
