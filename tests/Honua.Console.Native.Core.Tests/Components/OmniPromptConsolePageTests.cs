using Bunit;
using Honua.Console.Shell.Pages;

namespace Honua.Console.Native.Core.Tests.Components;

public sealed class OmniPromptConsolePageTests
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
