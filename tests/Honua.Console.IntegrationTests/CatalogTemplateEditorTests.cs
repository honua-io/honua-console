using AngleSharp.Html.Parser;
using Bunit;
using Bunit.TestDoubles;
using Honua.Console.Shell.Components;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

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
        Assert.Equal(hostile, page.Find("#template-source").GetAttribute("value"));
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
        var original = page.Find("#template-source").GetAttribute("value");
        page.Find("#template-source").Input("<h1>Local draft</h1>");
        Assert.True(page.FindComponent<UnsavedChangesGuard>().Instance.IsDirty);
        Assert.Contains("Unsaved local changes", page.Find(".catalog-template-status").TextContent);
        CatalogAdminPreview.Capture("template-dirty", page.Markup);
        page.Find("button[data-template-insert]").Click();
        Assert.Contains("<honua-catalog-search", page.Find("#template-source").GetAttribute("value"));
        Assert.Empty(page.FindAll("honua-catalog-search"));
        page.Find("button[data-template-discard]").Click();
        CatalogAdminPreview.Capture("template-confirmation", page.Markup);
        Assert.Contains("Local draft", page.Find("#template-source").GetAttribute("value"));
        page.Find("button[data-template-keep]").Click();
        Assert.True(page.FindComponent<UnsavedChangesGuard>().Instance.IsDirty);
        page.Find("button[data-template-discard]").Click();
        page.Find("button[data-template-confirm]").Click();
        Assert.Equal(original, page.Find("#template-source").GetAttribute("value"));
        Assert.False(page.FindComponent<UnsavedChangesGuard>().Instance.IsDirty);
    }

    [Theory]
    [InlineData("honua-catalog-search", null)]
    [InlineData("honua-service-card", "service")]
    [InlineData("honua-layer-card", "layer")]
    [InlineData("honua-dataset-card", "dataset")]
    [InlineData("honua-map-card", "map")]
    [InlineData("honua-app-card", "app")]
    public void Insert_RoundTripsHostileCatalogIdentityAndTheCardPlaceholder(string tag, string? identityAttribute)
    {
        const string catalogId = "a\"b<c&d</textarea>";
        using var context = CatalogAuthoringFixture.Context(CatalogAuthoringFixture.Read());
        var page = context.Render<CatalogTemplateEditorView>(parameters => parameters.Add(p => p.Template,
            CatalogAuthoringFixture.Template() with { CatalogId = catalogId, Source = string.Empty }));
        page.Find($"button[aria-label='Insert {tag}']").Click();
        var document = new HtmlParser().ParseDocument(Assert.IsType<string>(page.Find("#template-source").GetAttribute("value")));
        var element = Assert.Single(document.Body!.Children);
        Assert.Equal(tag, element.LocalName);
        Assert.Equal(catalogId, element.GetAttribute("catalog"));
        Assert.Equal(identityAttribute is null ? 1 : 2, element.Attributes.Length);
        if (identityAttribute is not null) Assert.Equal("{{ item.id }}", element.GetAttribute(identityAttribute));
        Assert.Empty(page.FindAll(tag));
    }

    [Theory]
    [InlineData(false, NavigationState.Prevented)]
    [InlineData(true, NavigationState.Succeeded)]
    public void DirtyEditor_RespectsTheLeaveDecision(bool leave, NavigationState expected)
    {
        using var context = CatalogAuthoringFixture.Context(CatalogAuthoringFixture.Read());
        context.JSInterop.Setup<bool>("confirm", _ => true).SetResult(leave);
        var page = context.Render<CatalogTemplatesPage>(parameters => parameters.Add(p => p.CatalogId, "open").Add(p => p.TemplateId, "service & map"));
        page.Find("#template-source").Input("Local changes");
        var navigation = (BunitNavigationManager)context.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("/catalogs/open");
        Assert.Equal(expected, navigation.History.First().State);
        Assert.Contains(context.JSInterop.Invocations, call => call.Identifier == "confirm");
        if (!leave)
        {
            Assert.Equal("Local changes", page.Find("#template-source").GetAttribute("value"));
            Assert.Equal("service & map", page.FindComponent<CatalogTemplateEditorView>().Instance.Template.TemplateId);
        }
    }

    [Fact]
    public void DiscardedEditor_AllowsNavigationWithoutAPrompt()
    {
        using var context = CatalogAuthoringFixture.Context(CatalogAuthoringFixture.Read());
        var page = context.Render<CatalogTemplatesPage>(parameters => parameters.Add(p => p.CatalogId, "open").Add(p => p.TemplateId, "service & map"));
        page.Find("#template-source").Input("Local changes");
        page.Find("button[data-template-discard]").Click();
        page.Find("button[data-template-confirm]").Click();
        var navigation = (BunitNavigationManager)context.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("/catalogs/open");
        Assert.Equal(NavigationState.Succeeded, navigation.History.First().State);
        Assert.DoesNotContain(context.JSInterop.Invocations, call => call.Identifier == "confirm");
    }

    [Fact]
    public void EqualTemplateReread_DoesNotDiscardDirtySource()
    {
        using var context = CatalogAuthoringFixture.Context(CatalogAuthoringFixture.Read());
        var template = CatalogAuthoringFixture.Template();
        var page = context.Render<CatalogTemplateEditorView>(parameters => parameters.Add(p => p.Template, template));
        page.Find("#template-source").Input("Local changes");
        page.Render(parameters => parameters.Add(p => p.Template, template with { }));
        Assert.Equal("Local changes", page.Find("#template-source").GetAttribute("value"));
        Assert.True(page.FindComponent<UnsavedChangesGuard>().Instance.IsDirty);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void MissingVersionAndUnknownState_AreReportedNeutrally(string? version)
    {
        using var context = CatalogAuthoringFixture.Context(CatalogAuthoringFixture.Read());
        var page = context.Render<CatalogTemplateEditorView>(parameters => parameters.Add(p => p.Template,
            CatalogAuthoringFixture.Template() with { Version = version, State = "future-state" }));
        Assert.Equal("Not reported · Version Not reported", page.Find("h2 + p").TextContent);
    }

    [Theory]
    [InlineData("data-template-keep")]
    [InlineData("data-template-confirm")]
    public void DiscardChoice_ReturnsFocusToTheSource(string choice)
    {
        using var context = CatalogAuthoringFixture.Context(CatalogAuthoringFixture.Read());
        var page = context.Render<CatalogTemplatesPage>(parameters => parameters.Add(p => p.CatalogId, "open").Add(p => p.TemplateId, "service & map"));
        var sourceBeforeEditing = (AngleSharp.Dom.IElement)page.Find("#template-source").Clone();
        page.Find("#template-source").Input("Local changes");
        page.Find("button[data-template-discard]").Click();
        var previousFocusCalls = context.JSInterop.Invocations.Count(call => call.Identifier == "Blazor._internal.domWrapper.focus");
        page.Find($"button[{choice}]").Click();
        var focusCall = Assert.Single(context.JSInterop.Invocations.Where(call => call.Identifier == "Blazor._internal.domWrapper.focus").Skip(previousFocusCalls));
        focusCall.Arguments[0].ShouldBeElementReferenceTo(sourceBeforeEditing);
        Assert.Empty(page.FindAll("button[data-template-confirm]"));
    }

    [Fact]
    public void EditingAfterDiscardRequest_ClosesTheOldConfirmation()
    {
        using var context = CatalogAuthoringFixture.Context(CatalogAuthoringFixture.Read());
        var page = context.Render<CatalogTemplatesPage>(parameters => parameters.Add(p => p.CatalogId, "open").Add(p => p.TemplateId, "service & map"));
        var original = page.Find("#template-source").GetAttribute("value");
        page.Find("#template-source").Input("Local changes");
        var focusCalls = context.JSInterop.Invocations.Count(call => call.Identifier == "Blazor._internal.domWrapper.focus");
        page.Find("button[data-template-discard]").Click();
        var focusCall = Assert.Single(context.JSInterop.Invocations.Where(call => call.Identifier == "Blazor._internal.domWrapper.focus").Skip(focusCalls));
        focusCall.Arguments[0].ShouldBeElementReferenceTo(page.Find("button[data-template-keep]"));
        Assert.Single(page.FindAll("button[data-template-keep]"));
        page.Find("#template-source").Input(original);
        page.Find("#template-source").Input("Different changes");
        Assert.Empty(page.FindAll("button[data-template-confirm]"));
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
        { Template = CatalogAuthoringFixture.Template() with { TemplateId = "service & map", Source = "Stale private source" } });
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
