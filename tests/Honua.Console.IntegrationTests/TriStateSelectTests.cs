using Bunit;
using Honua.Console.Shell.Components;

namespace Honua.Console.IntegrationTests;

public sealed class TriStateSelectTests
{
    [Fact]
    public void NullLabel_DefaultRemainsUnchangedAndCatalogLabelIsText()
    {
        using var context = new BunitContext();
        var select = context.Render<TriStateSelect>(p => p.Add(x => x.Label, "Flag"));
        Assert.Equal("(unchanged)", select.Find("option").TextContent);
        select.Render(p => p.Add(x => x.NullLabel, "<img src=x>"));
        Assert.Equal("<img src=x>", select.Find("option").TextContent);
        Assert.Empty(select.FindAll("img"));
    }

    [Fact]
    public void AllThreeChoices_RoundTripThroughTheExistingEncoding()
    {
        using var context = new BunitContext();
        bool? current = null;
        var select = context.Render<TriStateSelect>(p => p.Add(x => x.Label, "Flag")
            .Add(x => x.ValueChanged, value => current = value));
        foreach (var (raw, expected) in new (string, bool?)[] { ("true", true), ("false", false), ("", null) })
        {
            select.Find("select").Change(raw);
            Assert.Equal(expected, current);
            select.Render(p => p.Add(x => x.Value, current));
            Assert.Equal(raw, select.Find("select").GetAttribute("value"));
        }
    }
}
