using Honua.Console.Web.Runtime;
using Microsoft.Extensions.Configuration;

namespace Honua.Console.IntegrationTests;

public sealed class ConsoleStaticHostConfigurationTests
{
    [Fact]
    public void BundledStudioIsEnabledForTheStaticHost()
    {
        var configuration = new ConfigurationBuilder();
        ConsoleStaticHostConfiguration.EnableBundledStudio(configuration);

        Assert.Equal("true", configuration.Build()["Studio:EmbedJsStudio"]);
    }
}
