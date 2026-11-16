using Bunit;
using Honua.Console.Contracts;
using Honua.Console.Shell.Components;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

public sealed partial class ShareManagePageRenderTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ShareManage_QueryChanges_LoadsCurrentItem(bool enabled)
    {
        var requested = new List<string>();
        var data = new FakeShareDataSource
        {
            LoadItem = id =>
            {
                requested.Add(id);
                return Task.FromResult(ItemLoad(id));
            }
        };
        using var ctx = CreateContext(data, new RenderOptions { V4 = new ConsoleV4Options { Enabled = enabled } });
        var page = OpenShare(ctx, "item-1");

        await NavigateShare(ctx, "item-2");

        AssertCurrentItem(page, "item-2");
        Assert.Equal("item-2", page.Find("input[placeholder='item-...']").GetAttribute("value"));
        await NavigateShare(ctx, "item-2", "#exports");
        Assert.Equal(new[] { "item-1", "item-2" }, requested);
    }

    [Fact]
    public async Task ShareManage_RemovingQuery_ClearsPreviousItemAndSecret()
    {
        using var ctx = CreateContext(MintData());
        var page = OpenShare(ctx, "item-1");
        page.Find("[data-share-panel='public-link'] button[type='submit']").Click();
        page.WaitForAssertion(() => Assert.Contains("opaque-secret-once", page.Markup, StringComparison.Ordinal));
        page.Find("input[type='datetime-local']").Change(FutureExpiry());

        await NavigateShare(ctx, null);

        Assert.Empty(page.FindAll("[data-share-access]"));
        Assert.DoesNotContain("opaque-secret-once", page.Markup, StringComparison.Ordinal);
        Assert.False(page.FindComponent<UnsavedChangesGuard>().Instance.IsDirty);
        Assert.Empty(page.Find("input[placeholder='item-...']").GetAttribute("value") ?? string.Empty);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ShareManage_OverlappingLoads_OnlyNewestLoadControlsState(bool olderCompletesFirst)
    {
        var first = Pending<ShareAccessLoad>();
        var second = Pending<ShareAccessLoad>();
        var data = new FakeShareDataSource { LoadItem = id => id == "item-1" ? first.Task : second.Task };
        using var ctx = CreateContext(data);
        var page = OpenShare(ctx, "item-1");

        await NavigateShare(ctx, "item-2");
        if (olderCompletesFirst)
        {
            await page.InvokeAsync(() => first.SetResult(ItemLoad("item-1")));
            Assert.Contains("Loading share access", page.Markup, StringComparison.Ordinal);
            Assert.Empty(page.FindAll("[data-share-access]"));
        }

        await page.InvokeAsync(() => second.SetResult(ItemLoad("item-2")));
        AssertCurrentItem(page, "item-2");
        if (!olderCompletesFirst)
        {
            await page.InvokeAsync(() => first.SetResult(ItemLoad("item-1")));
        }

        AssertCurrentItem(page, "item-2");
        Assert.DoesNotContain("Loading share access", page.Markup, StringComparison.Ordinal);
        Assert.False(OpenButton(page).HasAttribute("disabled"));
    }

    [Fact]
    public async Task ShareManage_QueryChangesDuringMint_DiscardsOldCommandResult()
    {
        var pending = Pending<ShareCommandResult>();
        var data = new FakeShareDataSource
        {
            LoadItem = id => Task.FromResult(ItemLoad(id)),
            PendingMint = pending.Task
        };
        using var ctx = CreateContext(data);
        var page = OpenShare(ctx, "item-1");
        page.Find("input[type='datetime-local']").Change(FutureExpiry());
        var submit = page.Find("[data-share-panel='public-link'] form").SubmitAsync();

        await NavigateShare(ctx, "item-2");
        AssertCurrentItem(page, "item-2");
        page.Find("input[type='datetime-local']").Change(FutureExpiry(14));
        pending.SetResult(MintData().Command);
        await submit;

        AssertCurrentItem(page, "item-2");
        Assert.DoesNotContain("opaque-secret-once", page.Markup, StringComparison.Ordinal);
        Assert.True(page.FindComponent<UnsavedChangesGuard>().Instance.IsDirty);
    }

    [Fact]
    public async Task ShareManage_OldCommandFinishesWhileLoading_DoesNotUnlockNewItem()
    {
        var command = Pending<ShareCommandResult>();
        var load = Pending<ShareAccessLoad>();
        var data = new FakeShareDataSource
        {
            LoadItem = id => id == "item-1" ? Task.FromResult(ItemLoad(id)) : load.Task,
            PendingMint = command.Task
        };
        using var ctx = CreateContext(data);
        var page = OpenShare(ctx, "item-1");
        var submit = page.Find("[data-share-panel='public-link'] form").SubmitAsync();

        await NavigateShare(ctx, "item-2");
        command.SetResult(MintData().Command);
        await submit;

        Assert.Contains("Loading share access", page.Markup, StringComparison.Ordinal);
        Assert.Empty(page.FindAll("[data-share-access]"));
        await page.InvokeAsync(() => load.SetResult(ItemLoad("item-2")));
        AssertCurrentItem(page, "item-2");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ShareManage_LateOpenDataRead_DoesNotReplaceCurrentDetail(bool unavailable)
    {
        var first = Pending<CatalogItemReadResult>();
        var catalog = new FakeCatalogClient(ItemDetail("item-2"))
        {
            OpenDataRead = id => id == "item-1" ? first.Task : Task.FromResult(CatalogItemReadResult.Allowed(ItemDetail(id)))
        };
        var data = new FakeShareDataSource { LoadItem = id => Task.FromResult(ItemLoad(id, "public-indexed")) };
        using var ctx = CreateContext(data, new RenderOptions { Catalog = catalog });
        var page = OpenShare(ctx, "item-1");
        var tab = page.Find("[data-share-tab='opendata']").ClickAsync(new());

        await NavigateShare(ctx, "item-2");
        page.WaitForAssertion(() => Assert.Contains("Metadata for item-2", page.Markup, StringComparison.Ordinal));
        first.SetResult(unavailable
            ? CatalogItemReadResult.Denied(CatalogReadStatus.Unavailable, "old request unavailable")
            : CatalogItemReadResult.Allowed(ItemDetail("item-1")));
        await tab;

        Assert.Contains("Metadata for item-2", page.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Metadata for item-1", page.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("not bound to honua-server", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ShareManage_OpeningAnotherItem_ClearsPreviousExpiryAndSecret()
    {
        var data = MintData();
        using var ctx = CreateContext(data);
        var page = OpenShare(ctx, "item-1");
        page.Find("input[type='datetime-local']").Change(FutureExpiry());
        page.Find("[data-share-panel='public-link'] button[type='submit']").Click();
        page.WaitForAssertion(() => Assert.Contains("opaque-secret-once", page.Markup, StringComparison.Ordinal));
        data.Load = ItemLoad("item-2");
        page.Find("input[placeholder='item-...']").Change("item-2");

        OpenButton(page).Click();

        AssertCurrentItem(page, "item-2");
        Assert.Empty(page.Find("input[type='datetime-local']").GetAttribute("value") ?? string.Empty);
        Assert.DoesNotContain("opaque-secret-once", page.Markup, StringComparison.Ordinal);
        Assert.False(page.FindComponent<UnsavedChangesGuard>().Instance.IsDirty);
    }

    private static TaskCompletionSource<T> Pending<T>() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static ShareAccessLoad ItemLoad(string id, string tier = "public-link") =>
        new(PublicShare(tier, false, false, true, true) with { ItemId = id, ItemTitle = id }, []);

    private static ConsoleContentDetail ItemDetail(string id) => new()
    {
        Summary = new ConsoleContentSummary { Id = id, Title = id, Type = "layer" },
        Description = $"Metadata for {id}"
    };

    private static IRenderedComponent<ShareManagePage> OpenShare(BunitContext ctx, string id)
    {
        ctx.JSInterop.Setup<bool>("confirm", _ => true).SetResult(true);
        var navigation = ctx.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo(navigation.GetUriWithQueryParameter("itemId", id));
        return ctx.Render<ShareManagePage>();
    }

    private static Task NavigateShare(BunitContext ctx, string? id, string fragment = "") =>
        ctx.Renderer.Dispatcher.InvokeAsync(() =>
        {
            var navigation = ctx.Services.GetRequiredService<NavigationManager>();
            navigation.NavigateTo(navigation.GetUriWithQueryParameter("itemId", id) + fragment);
        });

    private static AngleSharp.Dom.IElement OpenButton(IRenderedComponent<ShareManagePage> page) =>
        page.FindAll("button").Single(button => button.TextContent.Contains("Open share access", StringComparison.Ordinal));

    private static void AssertCurrentItem(IRenderedComponent<ShareManagePage> page, string id) =>
        page.WaitForAssertion(() => Assert.Equal(id, page.Find("[data-share-access]").GetAttribute("data-share-access")));
}
