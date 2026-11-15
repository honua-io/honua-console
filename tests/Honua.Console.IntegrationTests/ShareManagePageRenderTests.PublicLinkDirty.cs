using Bunit;
using Honua.Console.Shell.Components;
using Honua.Console.Shell.Models;

namespace Honua.Console.IntegrationTests;

public sealed partial class ShareManagePageRenderTests
{
    [Fact]
    public void ShareManage_RejectedMint_PreservesUnsavedExpiry()
    {
        var data = MintData();
        data.Command = new ShareCommandResult(null, null, null, [MissingBinding]);
        var page = Render(data, "item-1");
        page.Find("input[type='datetime-local']").Change(FutureExpiry());

        page.Find("[data-share-panel='public-link'] button[type='submit']").Click();

        page.WaitForAssertion(() => Assert.True(page.FindComponent<UnsavedChangesGuard>().Instance.IsDirty));
        Assert.Contains("Missing binding", page.Markup, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ShareManage_EditDuringMint_PreservesUnsavedChanges(bool editItemId)
    {
        var data = MintData();
        var pending = new TaskCompletionSource<ShareCommandResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        data.PendingMint = pending.Task;
        var page = Render(data, "item-1");
        page.Find("input[type='datetime-local']").Change(FutureExpiry());
        var submit = page.Find("[data-share-panel='public-link'] form").SubmitAsync();
        page.WaitForAssertion(() => Assert.True(page.Find("[data-share-panel='public-link'] button[type='submit']").HasAttribute("disabled")));
        if (editItemId)
        {
            page.Find("input[placeholder='item-...']").Change("item-2");
        }
        else
        {
            page.Find("input[type='datetime-local']").Change(FutureExpiry(14));
        }

        pending.SetResult(data.Command);
        await submit;

        page.WaitForAssertion(() => Assert.Contains("opaque-secret-once", page.Markup, StringComparison.Ordinal));
        Assert.True(page.FindComponent<UnsavedChangesGuard>().Instance.IsDirty);
    }

    [Fact]
    public void ShareManage_MintDoesNotSaveEditedItemId()
    {
        var page = Render(MintData(), "item-1");
        page.Find("input[placeholder='item-...']").Change("item-2");
        page.Find("input[type='datetime-local']").Change(FutureExpiry());

        page.Find("[data-share-panel='public-link'] button[type='submit']").Click();

        page.WaitForAssertion(() => Assert.Contains("opaque-secret-once", page.Markup, StringComparison.Ordinal));
        Assert.True(page.FindComponent<UnsavedChangesGuard>().Instance.IsDirty);
    }

    private static string FutureExpiry(int days = 7) =>
        DateTime.UtcNow.AddDays(days).ToString("yyyy-MM-ddTHH:mm:ss");

    private static FakeShareDataSource MintData() => new()
    {
        Load = new ShareAccessLoad(PublicShare("public-link", false, false, true, true), []),
        Command = new ShareCommandResult(PublicShare("public-link", true, false, true, true),
            new ShareMintedSecret("public-link", "tok-2", "opaque-secret-once", null, null), null, [])
    };
}
