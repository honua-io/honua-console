using Bunit;
using Honua.Console.Contracts;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

public sealed class OperateLayerNavigationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NavigationClearsThePreviousLayersDomainDraft(bool enabled)
    {
        var fields = new NavigationFields();
        using var context = CreateContext(fields, enabled);
        var page = context.Render<OperateLayerDetailPage>(p => p.Add(c => c.ResourceId, "first"));
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll("[data-domain-save]")));
        page.Find("[data-domain-type]").Change(ConsoleDomainKind.Range.ToString());
        page.Find("[data-domain-name]").Change("first_range");
        page.Find("[data-domain-range-min]").Change("1");
        page.Find("[data-domain-range-max]").Change("10");
        page.Find("[data-domain-default-value]").Change("5");
        page.Find("[data-domain-default-clear]").Change(true);

        page.Render(p => p.Add(c => c.ResourceId, "second"));

        page.WaitForAssertion(() => Assert.Equal("second_field", page.Find("[data-domain-field]").GetAttribute("value")));
        Assert.Equal(string.Empty, page.Find("[data-domain-name]").GetAttribute("value"));
        Assert.Equal(ConsoleDomainKind.CodedValue.ToString(), page.Find("[data-domain-type]").GetAttribute("value"));
        Assert.Equal(string.Empty, page.Find("[data-domain-default-value]").GetAttribute("value"));
        Assert.False(page.Find("[data-domain-default-clear]").HasAttribute("checked"));
        page.Find("[data-domain-name]").Change("second_codes");
        page.Find("[data-domain-save]").Click();
        var update = Assert.Single(fields.DomainUpdates);
        Assert.Equal(8, update.LayerId);
        Assert.Equal("second_field", update.Authoring.FieldName);
        Assert.Equal("second_codes", update.Authoring.DomainName);
        Assert.Null(update.Authoring.RangeMin);
        Assert.Null(update.Authoring.RangeMax);
        Assert.Equal(ConsoleDefaultValueIntent.Unchanged, update.Authoring.DefaultValueIntent);
    }

    [Fact]
    public async Task PreviousLayersDelayedFieldReadCannotReplaceTheCurrentFields()
    {
        var fields = new NavigationFields { ReadGate = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        using var context = CreateContext(fields, true);
        var page = context.Render<OperateLayerDetailPage>(p => p.Add(c => c.ResourceId, "first"));
        page.WaitForAssertion(() => Assert.Contains(7, fields.Reads));
        page.Render(p => p.Add(c => c.ResourceId, "second"));
        page.WaitForAssertion(() => Assert.Equal("second_field", page.Find("[data-domain-field]").GetAttribute("value")));

        var renderCount = page.RenderCount;
        await page.InvokeAsync(() => fields.ReadGate.SetResult(NavigationFields.FieldsFor(7)));
        page.WaitForState(() => page.RenderCount > renderCount);

        page.WaitForAssertion(() => Assert.Equal("second_field", page.Find("[data-domain-field]").GetAttribute("value")));
        Assert.DoesNotContain("first_field", page.Find("[data-domain-field]").TextContent, StringComparison.Ordinal);
        Assert.Contains("Second", page.Find("h1").TextContent, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PreviousLayersDelayedSaveCannotReplaceTheCurrentDraft(bool domain)
    {
        var fields = new NavigationFields { SaveGate = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        using var context = CreateContext(fields, true);
        var page = context.Render<OperateLayerDetailPage>(p => p.Add(c => c.ResourceId, "first"));
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll("[data-domain-save]")));
        if (domain) page.Find("[data-domain-name]").Change("first_codes");
        else page.Find("[data-field-alias]").Change("First alias");
        var save = page.Find(domain ? "[data-domain-save]" : "[data-field-save]").ClickAsync(new());
        page.WaitForAssertion(() => Assert.Single(fields.SavedLayerIds));
        page.Render(p => p.Add(c => c.ResourceId, "second"));
        page.WaitForAssertion(() => Assert.Equal("second_field", page.Find("[data-domain-field]").GetAttribute("value")));
        page.Find("[data-domain-name]").Change("second_codes");
        page.Find("[data-field-alias]").Change("Second alias");

        await page.InvokeAsync(() => fields.SaveGate.SetResult(new() { Succeeded = true, State = "Updated" }));
        await save;

        page.WaitForAssertion(() => Assert.Empty(page.FindAll("[data-domain-result], [data-field-result]")));
        Assert.Equal("second_codes", page.Find("[data-domain-name]").GetAttribute("value"));
        Assert.Equal("Second alias", page.Find("[data-field-alias]").GetAttribute("value"));
        Assert.Equal(new[] { 7, 8 }, fields.Reads);
        Assert.Equal(7, Assert.Single(fields.SavedLayerIds));
        Assert.Equal(2, context.Services.GetRequiredService<ConsoleAgentContextRegistry>().Current.Unsaved.ChangedFieldCount);
    }

    private static BunitContext CreateContext(NavigationFields fields, bool enabled)
    {
        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddSingleton<NavigationManager>(new MountedNavigationManager("/console/"));
        context.AddConsoleNotifications();
        context.Services.AddSingleton(new ConsoleV4Options { Enabled = enabled });
        context.Services.AddSingleton<ConsoleAgentContextRegistry>();
        context.Services.AddSingleton<IConsoleLayerFieldsOperation>(fields);
        context.Services.AddSingleton<IOperateTransitionDataSource>(new FixedServicesDataSource(
            new OperateServiceDetail("terrain", "Terrain", "FeatureServer", "Running", "server",
                [new(7, "First", "Polygon", "first", "first"), new(8, "Second", "Polygon", "second", "second")], [], [])));
        return context;
    }

    private sealed class NavigationFields : IConsoleLayerFieldsOperation
    {
        public TaskCompletionSource<ConsoleLayerFields>? ReadGate { get; init; }
        public TaskCompletionSource<ConsoleSetDomainResult>? SaveGate { get; init; }
        public List<int> Reads { get; } = [];
        public List<int> SavedLayerIds { get; } = [];
        public List<(int LayerId, ConsoleDomainAuthoring Authoring)> DomainUpdates { get; } = [];

        public static ConsoleLayerFields FieldsFor(int layerId) => new()
        {
            Bound = true,
            LayerId = layerId,
            Fields = [new() { Name = layerId == 7 ? "first_field" : "second_field", Type = "esriFieldTypeString" }]
        };

        public Task<ConsoleLayerFields> GetFieldsAsync(int layerId, CancellationToken cancellationToken = default)
        {
            Reads.Add(layerId);
            return layerId == 7 && ReadGate is { } gate ? gate.Task : Task.FromResult(FieldsFor(layerId));
        }

        public Task<ConsoleSetDomainResult> SetDomainAsync(int layerId, ConsoleDomainAuthoring authoring,
            CancellationToken cancellationToken = default)
        {
            DomainUpdates.Add((layerId, authoring));
            return Save(layerId);
        }

        public Task<ConsoleSetDomainResult> SetFieldConfigurationAsync(int layerId, string fieldName,
            string? alias, bool hidden, CancellationToken cancellationToken = default) => Save(layerId);

        public Task<ConsoleSetDomainResult> SetCodedValueDomainAsync(int layerId, string fieldName,
            string domainName, IReadOnlyList<ConsoleCodedValue> codedValues,
            CancellationToken cancellationToken = default) => Save(layerId);

        private Task<ConsoleSetDomainResult> Save(int layerId)
        {
            SavedLayerIds.Add(layerId);
            return SaveGate?.Task ?? Task.FromResult(new ConsoleSetDomainResult { Succeeded = true, State = "Updated" });
        }
    }
}
