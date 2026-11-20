using Honua.Console.Contracts;

namespace Honua.Console.Shell.Services;

// Maps the server's anonymous open-data projection onto the catalog view models. Every item here
// already passed the server's eligibility check, so it is public open data by construction. Links
// use the item id, which is the key the public item page reads.
internal static class OpenDataPageMapper
{
    public static ConsoleContentSummary ToSummary(HonuaOpenDataPage page, string? itemType)
    {
        ArgumentNullException.ThrowIfNull(page);

        return new ConsoleContentSummary
        {
            Id = page.ItemId,
            Slug = page.ItemId,
            Type = string.IsNullOrWhiteSpace(itemType) ? string.Empty : ConsoleContentMapper.ToConsoleType(itemType),
            Title = string.IsNullOrWhiteSpace(page.Title) ? page.ItemId : page.Title,
            Summary = page.Description ?? string.Empty,
            Owner = page.PublisherName ?? string.Empty,
            Tags = [.. page.Tags],
            Formats = Formats(page),
            Modified = page.UpdatedAt ?? DateTimeOffset.UnixEpoch,
            Access = new ConsoleShareAccess
            {
                Sharing = CatalogSharingTiers.Public,
                OpenData = true
            }
        };
    }

    public static ConsoleContentDetail ToDetail(HonuaOpenDataPage page) => new()
    {
        Summary = ToSummary(page, itemType: null),
        Description = page.Description ?? string.Empty,
        Publications = [.. page.Distributions
            .Where(static distribution => IsWebUrl(distribution.AccessUrl))
            .Select(static distribution => new ConsoleContentPublication(distribution.AccessUrl, CatalogSharingTiers.Public, Embeddable: false, "published"))]
    };

    // Distribution URLs are server data shown on a public page; only plain web links are rendered.
    private static bool IsWebUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);

    private static string[] Formats(HonuaOpenDataPage page) =>
        [.. page.Distributions
            .Select(static distribution => distribution.Format ?? distribution.MediaType)
            .Where(static format => !string.IsNullOrWhiteSpace(format))
            .Select(static format => format!)
            .Distinct(StringComparer.OrdinalIgnoreCase)];
}
