using Bunit;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Pages;
using Honua.Console.Shell.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

public sealed class OperateLayerFieldDraftTests
{
    [Theory]
    [InlineData(false, SaveOperation.Field)]
    [InlineData(false, SaveOperation.Domain)]
    [InlineData(true, SaveOperation.Field)]
    [InlineData(true, SaveOperation.Domain)]
    public async Task SuccessfulSavePreservesEditsMadeDuringWrite(bool enabled, SaveOperation operation)
    {
        var fields = new DraftFields { DelayWrite = true };
        using var context = CreateContext(fields, enabled);
        var page = Render(context);
        page.Find("[data-field-alias]").Change("Submitted alias");
        if (operation == SaveOperation.Domain) page.Find("[data-domain-name]").Change("codes");
        var save = page.Find(operation == SaveOperation.Domain ? "[data-domain-save]" : "[data-field-save]").ClickAsync(new());
        page.WaitForAssertion(() => Assert.Equal(1, fields.Writes));
        page.Find("[data-field-alias]").Change("Newer alias");
        page.Find("[data-field-hidden]").Change(true);

        await page.InvokeAsync(() => fields.WriteGate.SetResult(Success()));
        await save;

        AssertDraft(page, "Newer alias", true);
        Assert.Equal(enabled ? 2 : 0, ChangedFields(context));
        if (operation == SaveOperation.Field) Assert.Equal(("first_field", "Submitted alias", false), fields.Submitted);
    }

    [Theory]
    [InlineData(false, SaveOperation.Field)]
    [InlineData(false, SaveOperation.Domain)]
    [InlineData(true, SaveOperation.Field)]
    [InlineData(true, SaveOperation.Domain)]
    public async Task SuccessfulSavePreservesEditsMadeDuringReadBack(bool enabled, SaveOperation operation)
    {
        var fields = new DraftFields { DelayReadBack = true };
        using var context = CreateContext(fields, enabled);
        var page = Render(context);
        page.Find("[data-field-alias]").Change("Submitted alias");
        if (operation == SaveOperation.Domain) page.Find("[data-domain-name]").Change("codes");
        var save = page.Find(operation == SaveOperation.Domain ? "[data-domain-save]" : "[data-field-save]").ClickAsync(new());
        page.WaitForAssertion(() => Assert.Equal(2, fields.Reads));
        page.Find("[data-field-alias]").Change("Read-back draft");
        page.Find("[data-field-hidden]").Change(true);

        await page.InvokeAsync(() => fields.ReadGate.SetResult(fields.Current));
        await save;

        AssertDraft(page, "Read-back draft", true);
        Assert.Equal(enabled ? 2 : 0, ChangedFields(context));
    }

    [Theory]
    [InlineData(false, SaveOperation.Field)]
    [InlineData(false, SaveOperation.Domain)]
    [InlineData(true, SaveOperation.Field)]
    [InlineData(true, SaveOperation.Domain)]
    public void SuccessfulSavePreservesAnotherFieldsDraft(bool enabled, SaveOperation operation)
    {
        var fields = new DraftFields();
        using var context = CreateContext(fields, enabled);
        var page = Render(context);
        var other = page.Find("[data-field-name='other_field']");
        other.QuerySelector("[data-field-alias]")!.Change("Other draft");
        other.QuerySelector("[data-field-hidden]")!.Change(true);
        if (operation == SaveOperation.Domain) page.Find("[data-domain-name]").Change("codes");
        else page.Find("[data-field-alias]").Change("Saved alias");

        page.Find(operation == SaveOperation.Domain ? "[data-domain-save]" : "[data-field-save]").Click();

        AssertDraft(page, operation == SaveOperation.Domain ? "Original alias" : "Saved alias", false);
        Assert.Equal("Other draft", page.Find("[data-field-name='other_field'] [data-field-alias]").GetAttribute("value"));
        Assert.True(page.Find("[data-field-name='other_field'] [data-field-hidden]").HasAttribute("checked"));
        Assert.Equal(enabled ? 2 : 0, ChangedFields(context));
    }

    [Theory]
    [InlineData(false, EditedProperty.Hidden)]
    [InlineData(false, EditedProperty.Alias)]
    [InlineData(true, EditedProperty.Hidden)]
    [InlineData(true, EditedProperty.Alias)]
    public async Task UnchangedInputsAcceptServerValuesIndependently(bool enabled, EditedProperty editedProperty)
    {
        var fields = new DraftFields { DelayWrite = true, Canonicalize = true };
        using var context = CreateContext(fields, enabled);
        var page = Render(context);
        page.Find("[data-field-alias]").Change("Submitted alias");
        if (editedProperty == EditedProperty.Hidden) page.Find("[data-field-hidden]").Change(true);
        var save = page.Find("[data-field-save]").ClickAsync(new());
        page.WaitForAssertion(() => Assert.Equal(1, fields.Writes));
        if (editedProperty == EditedProperty.Alias) page.Find("[data-field-alias]").Change("Newer alias");
        else page.Find("[data-field-hidden]").Change(false);

        await page.InvokeAsync(() => fields.WriteGate.SetResult(Success()));
        await save;

        AssertDraft(page, editedProperty == EditedProperty.Alias ? "Newer alias" : "Canonical alias", editedProperty == EditedProperty.Alias);
        Assert.Equal(enabled ? 1 : 0, ChangedFields(context));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SavedInputsAdoptCanonicalServerValuesAndClearDirtyState(bool enabled)
    {
        var fields = new DraftFields { Canonicalize = true };
        using var context = CreateContext(fields, enabled);
        var page = Render(context);
        page.Find("[data-field-alias]").Change("Submitted alias");

        page.Find("[data-field-save]").Click();

        AssertDraft(page, "Canonical alias", true);
        Assert.Equal(0, ChangedFields(context));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedSaveKeepsTheLatestDraftWithoutReloading(bool enabled)
    {
        var fields = new DraftFields { DelayWrite = true };
        using var context = CreateContext(fields, enabled);
        var page = Render(context);
        page.Find("[data-field-alias]").Change("Submitted alias");
        var save = page.Find("[data-field-save]").ClickAsync(new());
        page.WaitForAssertion(() => Assert.Equal(1, fields.Writes));
        page.Find("[data-field-alias]").Change("Retry alias");

        await page.InvokeAsync(() => fields.WriteGate.SetResult(new() { Succeeded = false, State = "Failed" }));
        await save;

        AssertDraft(page, "Retry alias", false);
        Assert.Equal(1, fields.Reads);
        Assert.Equal(enabled ? 1 : 0, ChangedFields(context));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RevertingDuringWriteRemainsDirtyUntilTheNextSave(bool enabled)
    {
        var fields = new DraftFields { DelayWrite = true };
        using var context = CreateContext(fields, enabled);
        var page = Render(context);
        page.Find("[data-field-alias]").Change("Submitted alias");
        var save = page.Find("[data-field-save]").ClickAsync(new());
        page.WaitForAssertion(() => Assert.Equal(1, fields.Writes));
        page.Find("[data-field-alias]").Change("Original alias");

        await page.InvokeAsync(() => fields.WriteGate.SetResult(Success()));
        await save;

        AssertDraft(page, "Original alias", false);
        Assert.Equal(enabled ? 1 : 0, ChangedFields(context));
        await page.Find("[data-field-save]").ClickAsync(new());
        Assert.Equal(("first_field", "Original alias", false), fields.Submitted);
        Assert.Equal(2, fields.Writes);
        Assert.Equal(0, ChangedFields(context));
    }

    public enum SaveOperation
    {
        Field,
        Domain
    }

    public enum EditedProperty
    {
        Alias,
        Hidden
    }

    private static ConsoleSetDomainResult Success() => new() { Succeeded = true, State = "Updated" };

    private static int ChangedFields(BunitContext context) =>
        context.Services.GetRequiredService<ConsoleAgentContextRegistry>().Current.Unsaved.ChangedFieldCount;

    private static void AssertDraft(IRenderedComponent<OperateLayerDetailPage> page, string alias, bool hidden)
    {
        Assert.Equal(alias, page.Find("[data-field-alias]").GetAttribute("value"));
        Assert.Equal(hidden, page.Find("[data-field-hidden]").HasAttribute("checked"));
    }

    private static IRenderedComponent<OperateLayerDetailPage> Render(BunitContext context)
    {
        var page = context.Render<OperateLayerDetailPage>(p => p.Add(c => c.ResourceId, "first"));
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll("[data-domain-save]")));
        return page;
    }

    private static BunitContext CreateContext(DraftFields fields, bool enabled)
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
                [new(7, "First", "Polygon", "first", "first")], [], [])));
        return context;
    }

    private sealed class DraftFields : IConsoleLayerFieldsOperation
    {
        public bool DelayWrite { get; init; }
        public bool DelayReadBack { get; init; }
        public bool Canonicalize { get; init; }
        public int Writes { get; private set; }
        public int Reads { get; private set; }
        public (string Name, string? Alias, bool Hidden)? Submitted { get; private set; }
        public TaskCompletionSource<ConsoleSetDomainResult> WriteGate { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<ConsoleLayerFields> ReadGate { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ConsoleLayerFields Current { get; private set; } = new()
        {
            Bound = true,
            LayerId = 7,
            Fields =
            [
                new() { Name = "first_field", Alias = "Original alias", Type = "esriFieldTypeString" },
                new() { Name = "other_field", Type = "esriFieldTypeString" }
            ]
        };

        public Task<ConsoleLayerFields> GetFieldsAsync(int layerId, CancellationToken cancellationToken = default)
        {
            Reads++;
            return DelayReadBack && Reads > 1 ? ReadGate.Task : Task.FromResult(Current);
        }

        public async Task<ConsoleSetDomainResult> SetFieldConfigurationAsync(int layerId, string fieldName,
            string? alias, bool hidden, CancellationToken cancellationToken = default)
        {
            Submitted = (fieldName, alias, hidden);
            var result = await Save();
            if (result.Succeeded)
            {
                Current = Current with
                {
                    Fields = Current.Fields.Select(field => field.Name == fieldName
                        ? field with { Alias = Canonicalize ? "Canonical alias" : alias, Hidden = Canonicalize || hidden }
                        : field).ToArray()
                };
            }
            return result;
        }

        public Task<ConsoleSetDomainResult> SetDomainAsync(int layerId, ConsoleDomainAuthoring authoring,
            CancellationToken cancellationToken = default) => Save();

        public Task<ConsoleSetDomainResult> SetCodedValueDomainAsync(int layerId, string fieldName,
            string domainName, IReadOnlyList<ConsoleCodedValue> codedValues,
            CancellationToken cancellationToken = default) => Save();

        private Task<ConsoleSetDomainResult> Save()
        {
            Writes++;
            return DelayWrite ? WriteGate.Task : Task.FromResult(Success());
        }
    }
}
