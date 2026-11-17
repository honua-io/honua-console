namespace Honua.Console.IntegrationTests;

// The PostGIS-only serving image refuses to start when appsettings.Test.json's warehouse providers stay on.
public sealed class LiveServerIntegrationOptionsTests
{
    [Theory]
    [InlineData("Experimental__Features__RedshiftProvider")]
    [InlineData("Experimental__Features__SnowflakeProvider")]
    public void BuildServerEnvironment_DisablesExperimentalWarehouseProvidersByDefault(string key)
    {
        var environment = new LiveServerIntegrationOptions().BuildServerEnvironment("Host=postgres");

        Assert.Equal("false", environment[key]);
    }

    [Fact]
    public void BuildServerEnvironment_LetsServerEnvironmentReenableAWarehouseProvider()
    {
        var options = new LiveServerIntegrationOptions
        {
            ServerEnvironment = "ASPNETCORE_ENVIRONMENT=Test;Experimental__Features__SnowflakeProvider=true"
        };

        var environment = options.BuildServerEnvironment("Host=postgres");

        Assert.Equal("true", environment["Experimental__Features__SnowflakeProvider"]);
        Assert.Equal("false", environment["Experimental__Features__RedshiftProvider"]);
    }
}
