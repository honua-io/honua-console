using Bunit;
using Honua.Console.Contracts;
using Honua.Console.Shell.Components.Operate;
using Honua.Console.Shell.Services;
using Honua.Sdk.Admin.Models;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

public sealed class PublishOutcomeVerificationRenderTests
{
    [Theory]
    [InlineData(0, "0 layers")]
    [InlineData(1, "1 layer.")]
    [InlineData(2, "2 layers")]
    public void ReadbackReportsTheRequestedServiceLayerCount(int layerCount, string expected)
    {
        using var context = new BunitContext();
        context.Services.AddSingleton<IConsoleAuthorityBindingValidator>(new Authority());
        var source = new PublicationNavigationSource
        {
            ServiceSummaries = [new ServiceSummary { ServiceName = "survey", LayerCount = layerCount }]
        };
        context.Services.AddSingleton<IConsolePublicationNavigationSource>(source);
        var panel = Render(context);

        panel.Find("[data-publish-verify]").Click();

        Assert.Contains(expected, panel.Find("[data-publish-readback]").TextContent);
        Assert.DoesNotContain("1 layers", panel.Markup);
        Assert.Equal(HonuaAdminCatalogQuery.First with { NameQuery = "survey" }, source.LastQuery);
        Assert.Equal(0, source.ChildReads);
        Assert.Equal(0, source.ConnectionReads);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("original", false)]
    public void MissingReviewedAuthorityNeverReadsOrUnlocks(string? binding, bool registerValidator)
    {
        using var context = new BunitContext();
        if (registerValidator) context.Services.AddSingleton<IConsoleAuthorityBindingValidator>(new Authority());
        var source = new PublicationNavigationSource();
        context.Services.AddSingleton<IConsolePublicationNavigationSource>(source);
        var completions = 0;
        var panel = context.Render<PublishOutcomeVerification>(parameters => parameters
            .Add(component => component.AuthorityBinding, binding)
            .Add(component => component.ServiceName, "survey")
            .Add(component => component.LayerName, "Parcels")
            .Add(component => component.OnVerified, () => completions++));

        panel.Find("[data-publish-verify]").Click();

        Assert.Equal(0, source.ServiceReads);
        Assert.Equal(0, completions);
        Assert.Contains("Return to the original user and environment", panel.Markup);
        Assert.Empty(panel.FindAll("[data-publish-readback]"));
    }

    [Theory]
    [InlineData("binding")]
    [InlineData("service")]
    [InlineData("layer")]
    public async Task AChangedTargetCannotUnlockFromAnInFlightOriginalRead(string changedParameter)
    {
        using var context = new BunitContext();
        context.Services.AddSingleton<IConsoleAuthorityBindingValidator>(new Authority());
        var source = new PublicationNavigationSource { HoldCount = 1 };
        context.Services.AddSingleton<IConsolePublicationNavigationSource>(source);
        var completions = 0;
        var changes = new ConsoleFolderChanges();
        var reloads = 0;
        changes.Changed += () => reloads++;
        context.Services.AddSingleton(changes);
        var panel = context.Render<PublishOutcomeVerification>(parameters => parameters
            .Add(component => component.AuthorityBinding, "original")
            .Add(component => component.ServiceName, "survey")
            .Add(component => component.LayerName, "Parcels")
            .Add(component => component.OnVerified, () => completions++));
        var pending = panel.Find("[data-publish-verify]").ClickAsync(new MouseEventArgs());
        panel.WaitForAssertion(() => Assert.Equal(1, source.ServiceReads));
        Assert.True(panel.Find("[data-publish-verify]").HasAttribute("disabled"));
        panel.Render(parameters => parameters
            .Add(component => component.AuthorityBinding, changedParameter == "binding" ? "other" : "original")
            .Add(component => component.ServiceName, changedParameter == "service" ? "other" : "survey")
            .Add(component => component.LayerName, changedParameter == "layer" ? "other" : "Parcels"));

        source.ReleaseNext();
        await pending;

        Assert.Equal(0, completions);
        Assert.Equal(0, reloads);
        Assert.Empty(panel.FindAll("[data-publish-readback]"));
        Assert.Contains("original catalog could not be verified", panel.Markup);
        Assert.False(panel.Find("[data-publish-verify]").HasAttribute("disabled"));
    }

    [Fact]
    public void ARefusedReadCanBeRetriedWithoutUnlockingEarly()
    {
        using var context = new BunitContext();
        context.Services.AddSingleton<IConsoleAuthorityBindingValidator>(new Authority());
        var source = new PublicationNavigationSource
        {
            FailNext = OperateSectionResult<HonuaAdminServicePage>.Denied(OperateSectionStatus.Forbidden, "Catalog refused")
        };
        context.Services.AddSingleton<IConsolePublicationNavigationSource>(source);
        var completions = 0;
        var panel = context.Render<PublishOutcomeVerification>(parameters => parameters
            .Add(component => component.AuthorityBinding, "original")
            .Add(component => component.ServiceName, "service-499")
            .Add(component => component.LayerName, "Parcels")
            .Add(component => component.OnVerified, () => completions++));
        panel.Find("[data-publish-verify]").Click();
        Assert.Equal(0, completions);
        Assert.Contains("Catalog refused", panel.Markup);

        panel.Find("[data-publish-verify]").Click();

        Assert.Equal(1, completions);
        Assert.Equal(2, source.ServiceReads);
        Assert.Contains("10 layers", panel.Find("[data-publish-readback]").TextContent);
    }

    private static IRenderedComponent<PublishOutcomeVerification> Render(BunitContext context) =>
        context.Render<PublishOutcomeVerification>(parameters => parameters
            .Add(component => component.AuthorityBinding, "original")
            .Add(component => component.ServiceName, "survey")
            .Add(component => component.LayerName, "Parcels"));

    private sealed class Authority : IConsoleAuthorityBindingValidator
    {
        public Task<bool> IsCurrentAsync(string binding, CancellationToken cancellationToken = default) =>
            Task.FromResult(binding == "original");
    }
}
