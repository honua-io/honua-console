using Bunit;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

internal sealed class CatalogAuthoringFixture(CatalogAdminRead read) : ICatalogAdminSource
{
    public List<CatalogAdminRequest> Requests { get; } = [];

    public Task<CatalogAdminRead> ReadAsync(CatalogAdminRequest request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return Task.FromResult(read);
    }

    public static BunitContext Context(CatalogAdminRead read) => Context(new CatalogAuthoringFixture(read));

    public static BunitContext Context(ICatalogAdminSource source)
    {
        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddSingleton(new CatalogAdminOptions { Enabled = true });
        context.Services.AddSingleton(source);
        return context;
    }

    public static CatalogAdminRead Read() => new([], new("open", "Open catalog", new(1, 0, 0, 0, 1), []), "fixture", null)
    {
        Templates = new("open", [new("service & map", "Service page", "service") { State = "draft", DraftVersion = "v4", ReleasedVersion = "v3" }, new("home", "Home", "home")]),
        Template = Template(),
        Listing = Listing()
    };

    public static CatalogTemplateDocument Template() => new("open", "service & map", "Service page")
    {
        State = "draft",
        Version = "v4",
        Source = "<article><h1>{{ item.title }}</h1></article>"
    };

    public static CatalogListingDocument Listing() => new("open", "emergency", "Emergency management")
    {
        Kind = "service",
        CanAppear = true,
        Summary = "Shelters and evacuation routes",
        Description = "**Emergency shelters** are maintained year round.",
        Tags = ["flood", "shelters"],
        Licence = "CC BY 4.0",
        LicenceSource = "from folder Public Safety",
        ThumbnailSource = "Generated from extent and style",
        ThumbnailAlt = "Shelters and flood zones",
        Owner = "Emergency GIS",
        Contact = "gis@example.test",
        Memberships = [new("open", "Open catalog", "Folder Public Safety") { Audience = "public", Visibility = "listed", Category = "Public safety", Featured = true, HiddenHere = false },
            new("staff", "Staff", "Tag hazard") { Audience = "roles", Visibility = "restricted" }],
        RelatedTitles = ["Evacuation map"],
        Checks = [new("Thumbnail alt text", "Reported")]
    };
}
