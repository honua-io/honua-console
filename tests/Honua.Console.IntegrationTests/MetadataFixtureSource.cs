using Bunit;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

internal sealed class MetadataFixtureSource : IMetadataPreviewSource
{
    public List<MetadataPreviewRequest> Requests { get; } = [];
    public MetadataPreviewRead Read { get; set; } = Example();

    public Task<MetadataPreviewRead> ReadAsync(MetadataPreviewRequest request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return Task.FromResult(Read);
    }

    public static BunitContext Context() => Context(new MetadataFixtureSource());

    public static BunitContext Context(IMetadataPreviewSource source)
    {
        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddSingleton(new CatalogAdminOptions { Enabled = true });
        context.Services.AddSingleton(source);
        return context;
    }

    public static MetadataPreviewRead Example() => new("fixture")
    {
        Dataset = new("flood", "Flood hazard areas")
        {
            Context = "Dataset (Feature type) · Service context: Emergency · Folder: Public safety",
            Fields =
            [
                new("title", "Title", "Identification") { LocalValue = "Flood hazard areas", EffectiveValue = "Flood hazard areas", Editable = true, Source = "Set here", History = [Change()] },
                new("abstract", "Abstract", "Identification") { LocalValue = "Flood areas for planning.", EffectiveValue = "Flood areas for planning.", Editable = true, Source = "Set here" },
                new("keywords", "Keywords", "Identification") { EffectiveValue = "flood, risk", Source = "Set here" },
                new("contacts", "Contacts", "Contacts") { EffectiveValue = "pointOfContact · Emergency GIS · gis@example.test", Source = "Inherited from service Emergency" },
                new("licence", "Licence", "Licence and maintenance") { EffectiveValue = "CC BY 4.0", InheritedValue = "CC BY 4.0", HasInheritance = true, InheritedFrom = "Folder Public safety", Editable = true, Source = "Inherited from folder Public safety" },
                new("frequency", "Update frequency", "Licence and maintenance") { EffectiveValue = "monthly", InheritedValue = "monthly", HasInheritance = true, InheritedFrom = "Folder Public safety", Editable = true, Source = "Inherited from folder Public safety" },
                new("lineage", "Lineage", "Lineage and quality") { LocalValue = "Reviewed data.", EffectiveValue = "Reviewed data.", InheritedValue = "Maintained by Emergency GIS.", InheritedFrom = "Service Emergency", HasInheritance = true, Editable = true, Source = "Set here", History = [Change() with { Field = "Lineage", State = "Proposed", After = "Surveyed in 2026" }] },
                new("accuracy", "Positional accuracy", "Lineage and quality") { LocalValue = "± 5 m horizontal", EffectiveValue = "± 5 m horizontal", Editable = true, Source = "Set here" },
                new("extent", "Spatial extent", "Extent and resolution") { EffectiveValue = "-123.41, 46.72 → -122.86, 47.11 (EPSG:4326)", Source = "From data; refresh not reported" },
                new("resolution", "Spatial resolution", "Extent and resolution") { LocalValue = "1:12,000", EffectiveValue = "1:12,000", Editable = true, Source = "Set here" },
                new("distribution", "Distribution", "Distribution") { EffectiveValue = "OGC API Features · https://example.test/ogc/collections/flood", Source = "Service capability projection" },
                new("custom", "Retention schedule", "County fields") { EffectiveValue = "Keep while current + 6 years", Source = "Set here · custom schema county:retention" }
            ],
            History = [Change(), Change() with { Field = "Lineage", Actor = "Example MCP client", Client = "MCP on behalf of example user", State = "Proposed" }]
        },
        Schemas =
        [
            new("county:retention", "Retention schedule", "Choice list (2)") { Required = true, Kinds = ["Dataset"], Filled = "12 / 20", Output = "ISO extension · DCAT", Visibility = "Public records" },
            new("county:department", "Steward department", "Text") { Kinds = ["Dataset", "Service", "Folder"], Inherits = true, Visibility = "Signed-in only" },
            new("county:pii", "Contains personal data", "Yes / no") { Required = true, Kinds = ["Dataset"], Visibility = "Never (internal)" }
        ],
        Import = new("flood", "flood-metadata.xml", "ISO 19139")
        {
            Rows =
            [
                new("title", "Title", "identification/citation/title") { Current = "Flood hazard areas", Incoming = "Flood risk", Changed = true, Selected = true },
                new("frequency", "Update frequency", "maintenance/frequency") { Current = "monthly", Incoming = "asNeeded", InheritedFrom = "Folder Public safety", Changed = true, Selected = false }
            ],
            Unmapped = [new("extension/quality", "Source-specific quality notes", "No matching field; retain original attachment when import is supported.")]
        }
    };

    private static MetadataHistoryPreview Change() => new("Title", "Example user", "Console")
    {
        When = "Example recorded time",
        Before = "Flood areas",
        After = "Flood hazard areas",
        State = "Applied"
    };
}
