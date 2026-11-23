using Honua.Console.Web.Runtime;

namespace Honua.Console.IntegrationTests;

public sealed class ConsoleEmbedRoutesTests
{
    [Theory]
    [InlineData("https://localhost:18083/console/embed/maps/district")]
    [InlineData("https://localhost:18083/console/maps/published/district")]
    [InlineData("https://localhost:18083/console/Maps/Published/team/district?view=full")]
    public void AudienceRoutesRenderWithoutOperatorChrome(string uri)
    {
        Assert.True(ConsoleEmbedRoutes.IsEmbed(uri));
    }

    [Theory]
    [InlineData("https://localhost:18083/console/")]
    [InlineData("https://localhost:18083/console/maps/new")]
    [InlineData("https://localhost:18083/console/maps/district")]
    [InlineData("https://localhost:18083/console/inbox?proposalId=maps/published/x")]
    public void OperatorRoutesKeepTheConversationDrawer(string uri)
    {
        Assert.False(ConsoleEmbedRoutes.IsEmbed(uri));
    }
}
