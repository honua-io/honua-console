using Bunit;
using Honua.Console.Shell.Pages;

namespace Honua.Console.IntegrationTests;

public sealed class OmniPromptConsolePageRenderTests
{
    [Fact]
    public void EntryUsesSharedConversationWithoutClassifierOrSecondComposer()
    {
        using var context = new BunitContext();
        var page = context.Render<OmniPromptConsolePage>();
        Assert.Single(page.FindAll("[data-workspace-open]"));
        Assert.Empty(page.FindAll("textarea, [data-omni-confirm-studio], [data-omni-confirm-devops]"));
        Assert.NotEmpty(page.FindAll("a[href='/inbox']"));
        Assert.NotEmpty(page.FindAll("a[href='/studio']"));
        Assert.Empty(page.FindAll("a[href='/studio/proof']"));
    }
}
