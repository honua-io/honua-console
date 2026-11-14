using Bunit;
using Honua.Console.Shell.Layout;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

public sealed class ConsolePublicationAncestryRecoveryTests
{
    [Theory]
    [InlineData(OperateSectionStatus.Unavailable, true)]
    [InlineData(OperateSectionStatus.Forbidden, false)]
    public async Task NavigationRetriesTransientAncestryWithoutChangingAuthority(OperateSectionStatus failure, bool recovers)
    {
        using var context = new BunitContext();
        context.AddAuthorization().SetAuthorized("synthetic-operator");
        var source = new PublicationNavigationSource();
        context.Services.AddSingleton<IConsolePublicationNavigationSource>(source);
        var navigation = context.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("/operate/layers/connection-one-layer-7");
        var selection = new ConsolePublicationLayerContext();
        IReadOnlyList<ConsolePublicationNode> path = [];
        var tree = context.Render<CascadingValue<ConsolePublicationLayerContext>>(parameters => parameters
            .Add(component => component.Value, selection)
            .AddChildContent<ConsolePublicationNavigation>(child => child
                .Add(component => component.SelectionChanged, value => path = value)));
        tree.WaitForAssertion(() => Assert.Equal(1, source.ServiceReads));
        source.FailNext = OperateSectionResult<Honua.Console.Contracts.HonuaAdminServicePage>.Denied(failure, "Ancestry unavailable.");
        var layer = new OperateServiceLayer(7, "Bridges", "LineString", "connection-one-layer-7", "bridges");
        var service = new OperateServiceDetail("service-499", "Parcels", "FeatureServer", "Running", "server", [layer], [], []);
        await tree.InvokeAsync(() => selection.CompleteRead(selection.BeginRead(layer.CanonicalResourceId), [new(service, layer)]));
        tree.WaitForAssertion(() => Assert.Equal(2, source.ServiceReads));
        Assert.Empty(path);
        await tree.InvokeAsync(() => navigation.NavigateTo("/operate/layers/connection-one-layer-7?retry=1"));
        tree.WaitForAssertion(() => Assert.Equal(recovers ? 3 : 2, source.ServiceReads));
        if (recovers)
            tree.WaitForAssertion(() => Assert.Equal("Bridges", path.Last().Label));
        else
            Assert.Empty(path);
        Assert.Equal(0, source.ConnectionReads);
        Assert.Equal(0, source.ChildReads);
    }
}
