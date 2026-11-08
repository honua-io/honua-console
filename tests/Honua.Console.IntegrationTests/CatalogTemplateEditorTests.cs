using Bunit;
using Honua.Console.Shell.Components;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;

namespace Honua.Console.IntegrationTests;

public sealed class CatalogTemplateEditorTests
{
    [Fact]
    public void Source_IsInertEvenWhenItContainsClosingTextareaAndScripts()
    {
        const string hostile = "</textarea><script>alert(1)</script><img src=x onerror=alert(2)>";
        using var context = CatalogAuthoringFixture.Context(CatalogAuthoringFixture.Read() with
        { Template = CatalogAuthoringFixture.Template() with { Source = hostile } });
        var page = context.Render<CatalogTemplatesPage>(parameters => parameters.Add(p => p.CatalogId, "open").Add(p => p.TemplateId, "service & map"));
        Assert.Equal(hostile, page.Find("#template-source").TextContent);
        Assert.Empty(page.FindAll("script, img, iframe, [srcdoc], [onerror]"));
        Assert.Contains("Preview is unavailable", page.Markup);
        Assert.Contains("Saving and releasing are unavailable", page.Markup);
        Assert.Empty(page.FindAll("button[type='submit']"));
        CatalogAdminPreview.Capture("template-editor", page.Markup);
    }

    [Fact]
    public void LocalEditsAndComponentInsertionAreGuardedAndDiscardRequiresConfirmation()
    {
        using var context = CatalogAuthoringFixture.Context(CatalogAuthoringFixture.Read());
        var page = context.Render<CatalogTemplatesPage>(parameters => parameters.Add(p => p.CatalogId, "open").Add(p => p.TemplateId, "service & map"));
        var original = page.Find("#template-source").TextContent;
        page.Find("#template-source").Input("<h1>Local draft</h1>");
        Assert.True(page.FindComponent<UnsavedChangesGuard>().Instance.IsDirty);
        Assert.Contains("Unsaved local changes", page.Find(".catalog-template-status").TextContent);
        page.Find("button[data-template-insert]").Click();
        Assert.Contains("<honua-catalog-search", page.Find("#template-source").TextContent);
        Assert.Empty(page.FindAll("honua-catalog-search"));
        page.Find("button[data-template-discard]").Click();
        Assert.Contains("Local draft", page.Find("#template-source").TextContent);
        page.Find("button[data-template-keep]").Click();
        Assert.True(page.FindComponent<UnsavedChangesGuard>().Instance.IsDirty);
        page.Find("button[data-template-discard]").Click();
        page.Find("button[data-template-confirm]").Click();
        Assert.Equal(original, page.Find("#template-source").TextContent);
        Assert.False(page.FindComponent<UnsavedChangesGuard>().Instance.IsDirty);
    }

    [Fact]
    public void ComponentReference_AdvertisesOnlyTheCurrentDraftSearchAndCards()
    {
        using var context = CatalogAuthoringFixture.Context(CatalogAuthoringFixture.Read());
        var page = context.Render<CatalogTemplatesPage>(parameters => parameters.Add(p => p.CatalogId, "open").Add(p => p.TemplateId, "service & map"));
        Assert.Equal(6, page.FindAll("button[data-template-insert]").Count);
        Assert.DoesNotContain("extent-filter", page.Markup);
        Assert.DoesNotContain("honua-map-preview", page.Markup);
        Assert.DoesNotContain("honua-service-endpoints", page.Markup);
    }

    [Fact]
    public void WrongTemplateIdentity_DoesNotExposeSource()
    {
        using var context = CatalogAuthoringFixture.Context(CatalogAuthoringFixture.Read() with
        { Template = CatalogAuthoringFixture.Template() with { TemplateId = "private", Source = "Private source" } });
        var page = context.Render<CatalogTemplatesPage>(parameters => parameters.Add(p => p.CatalogId, "open").Add(p => p.TemplateId, "service & map"));
        Assert.DoesNotContain("Private source", page.Markup);
        Assert.Contains("Template is unavailable", page.Markup);
        Assert.Empty(page.FindAll("textarea"));
    }

    [Fact]
    public void SameCatalogTemplateNavigation_CancelsAndRejectsThePreviousReply()
    {
        var source = new DelayedSource();
        using var context = CatalogAuthoringFixture.Context(source);
        var page = context.Render<CatalogTemplatesPage>(parameters => parameters.Add(p => p.CatalogId, "open").Add(p => p.TemplateId, "old"));
        page.Render(parameters => parameters.Add(p => p.TemplateId, "service & map"));
        Assert.True(source.FirstToken.IsCancellationRequested);
        Assert.Contains("Service page", page.Markup);
        var renderCount = page.RenderCount;
        source.First.SetResult(CatalogAuthoringFixture.Read() with
        { Template = CatalogAuthoringFixture.Template() with { TemplateId = "old", Source = "Stale private source" } });
        page.WaitForState(() => page.RenderCount > renderCount);
        Assert.Contains("Service page", page.Markup);
        Assert.DoesNotContain("Stale private source", page.Markup);
    }

    private sealed class DelayedSource : ICatalogAdminSource
    {
        public TaskCompletionSource<CatalogAdminRead> First { get; } = new();
        public CancellationToken FirstToken { get; private set; }
        public Task<CatalogAdminRead> ReadAsync(CatalogAdminRequest request, CancellationToken cancellationToken)
        {
            if (request.TemplateId != "old") return Task.FromResult(CatalogAuthoringFixture.Read());
            FirstToken = cancellationToken;
            return First.Task;
        }
    }
}
