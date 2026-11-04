using Bunit;
using Honua.Console.Shell.Components.Operate;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

public sealed class GoLiveStepTests
{
    private const string ServiceName = "parcels";

    [Fact]
    public void Apply_SetsExactlyTheChosenProtocolsAndVisibilityAndRendersTheLiveRoutes()
    {
        using var ctx = NewContext();
        var services = Bind(ctx, new RecordingServiceConfiguration());

        var cut = ApplyGoLive(ctx, PublishVisibility.Public);

        Assert.Equal(ServiceName, services.LastProtocols?.ServiceName);
        Assert.Equal(["FeatureServer", "OgcFeatures"], services.LastProtocols?.EnabledProtocols);
        Assert.Equal(ServiceName, services.LastAccessPolicy?.ServiceName);
        Assert.True(services.LastAccessPolicy?.AllowAnonymous);
        Assert.Empty(services.LastAccessPolicy?.AllowedRoles ?? ["unset"]);
        Assert.Equal(
            ["/rest/services/parcels/FeatureServer", "/ogc/features"],
            cut.FindAll("[data-live-route]").Select(route => route.TextContent).ToArray());
        Assert.Single(cut.FindAll("[data-go-live-done]"));
        Assert.Empty(cut.FindAll("[data-step-error]"));
    }

    [Theory]
    [InlineData(PublishVisibility.Org, false, new string[0])]
    [InlineData(PublishVisibility.Private, false, new[] { "admin" })]
    public void Apply_MapsVisibilityToTheServiceAccessPolicy(PublishVisibility visibility, bool anonymous, string[] roles)
    {
        using var ctx = NewContext();
        var services = Bind(ctx, new RecordingServiceConfiguration());

        ApplyGoLive(ctx, visibility);

        Assert.Equal(anonymous, services.LastAccessPolicy?.AllowAnonymous);
        Assert.Equal(roles, services.LastAccessPolicy?.AllowedRoles);
    }

    [Fact]
    public void Apply_ProtocolTheServerDidNotEnable_IsReportedAsNotLive()
    {
        using var ctx = NewContext();
        Bind(ctx, new RecordingServiceConfiguration { EnabledAfterUpdate = ["FeatureServer"] });

        var cut = ApplyGoLive(ctx, PublishVisibility.Org);

        Assert.Equal("succeeded", cut.Find("[data-protocol-result='FeatureServer']").GetAttribute("data-state"));
        var ogc = cut.Find("[data-protocol-result='OgcFeatures']");
        Assert.Equal("failed", ogc.GetAttribute("data-state"));
        Assert.Contains("Not enabled", ogc.TextContent, StringComparison.Ordinal);
        Assert.Single(cut.FindAll("[data-step-error]"));
        Assert.Empty(cut.FindAll("[data-go-live-done]"));
    }

    [Fact]
    public void Apply_RejectedProtocolUpdate_ShowsTheServerStateAndDetail()
    {
        using var ctx = NewContext();
        Bind(ctx, new RecordingServiceConfiguration { ProtocolsResult = Rejected("Invalid protocol(s): OgcFeatures.") });

        var cut = ApplyGoLive(ctx, PublishVisibility.Org);

        Assert.All(cut.FindAll("[data-protocol-result]"), row =>
        {
            Assert.Equal("failed", row.GetAttribute("data-state"));
            Assert.Contains("Rejected", row.TextContent, StringComparison.Ordinal);
            Assert.Contains("Invalid protocol(s): OgcFeatures.", row.TextContent, StringComparison.Ordinal);
        });
        Assert.Empty(cut.FindAll("[data-live-route]"));
        Assert.Equal("succeeded", cut.Find("[data-visibility-result]").GetAttribute("data-state"));
    }

    [Fact]
    public void Apply_RejectedVisibility_KeepsTheLiveProtocolsAndFlagsTheFailure()
    {
        using var ctx = NewContext();
        Bind(ctx, new RecordingServiceConfiguration { AccessPolicyResult = Rejected("Service 'parcels' not found.") });

        var cut = ApplyGoLive(ctx, PublishVisibility.Public);

        Assert.Equal(2, cut.FindAll("[data-live-route]").Count);
        var visibility = cut.Find("[data-visibility-result]");
        Assert.Equal("failed", visibility.GetAttribute("data-state"));
        Assert.Contains("Service 'parcels' not found.", visibility.TextContent, StringComparison.Ordinal);
        Assert.Single(cut.FindAll("[data-step-error]"));
    }

    [Fact]
    public void Apply_WithoutAServerBinding_ReportsMissingBindingAndNoRoutes()
    {
        using var ctx = NewContext();
        ctx.Services.AddSingleton<IServiceConfigurationOperation>(new UnsupportedServiceConfigurationOperation());

        var cut = ApplyGoLive(ctx, PublishVisibility.Org);

        Assert.All(cut.FindAll("[data-protocol-result]"), row => Assert.Contains("Missing binding", row.TextContent, StringComparison.Ordinal));
        Assert.Contains("Missing binding", cut.Find("[data-visibility-result]").TextContent, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("[data-live-route]"));
        Assert.Single(cut.FindAll("[data-step-error]"));
    }

    [Fact]
    public void Apply_UsesTheVisibilityApprovedAtApply_AndLocksTheFormWhileItRuns()
    {
        using var ctx = NewContext();
        var gate = new TaskCompletionSource();
        var services = Bind(ctx, new RecordingServiceConfiguration { ProtocolsGate = gate.Task });
        var flow = Flow(PublishVisibility.Private);

        var cut = ctx.Render<GoLiveStep>(parameters => parameters.Add(step => step.Flow, flow));
        cut.Find("[data-go-live-apply]").Click();
        Assert.True(cut.Find("[data-visibility]").HasAttribute("disabled"));
        Assert.True(cut.Find("button:not([data-go-live-apply])").HasAttribute("disabled"));
        flow.Visibility = PublishVisibility.Public;
        gate.SetResult();

        cut.WaitForAssertion(() => Assert.NotNull(services.LastAccessPolicy));
        Assert.False(services.LastAccessPolicy!.AllowAnonymous);
        Assert.Equal(["admin"], services.LastAccessPolicy.AllowedRoles);
    }

    [Fact]
    public void Apply_DisabledLayer_IsReportedInsteadOfLive()
    {
        using var ctx = NewContext();
        Bind(ctx, new RecordingServiceConfiguration());
        var flow = Flow(PublishVisibility.Public);
        flow.Resource.LayerEnabled = false;

        var cut = ctx.Render<GoLiveStep>(parameters => parameters.Add(step => step.Flow, flow));
        cut.Find("[data-go-live-apply]").Click();

        var layer = cut.Find("[data-layer-result]");
        Assert.Equal("failed", layer.GetAttribute("data-state"));
        Assert.Contains("Disabled", layer.TextContent, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("[data-go-live-done]"));
        Assert.Single(cut.FindAll("[data-step-error]"));
    }

    [Fact]
    public void GoLive_SaysTheStyleIsNotApplied()
    {
        using var ctx = NewContext();
        Bind(ctx, new RecordingServiceConfiguration());

        var cut = ctx.Render<GoLiveStep>(parameters => parameters.Add(step => step.Flow, Flow(PublishVisibility.Org)));

        Assert.Contains("does not apply a style", cut.Find("[data-style-not-applied]").TextContent, StringComparison.Ordinal);
        Assert.Single(cut.FindAll("a[href='/operate/layers/conn-1-layer-7/style']"));
    }

    private static BunitContext NewContext()
    {
        var ctx = new BunitContext();
        ctx.AddConsoleNotifications();
        return ctx;
    }

    private static RecordingServiceConfiguration Bind(BunitContext ctx, RecordingServiceConfiguration services)
    {
        ctx.Services.AddSingleton<IServiceConfigurationOperation>(services);
        return services;
    }

    private static IRenderedComponent<GoLiveStep> ApplyGoLive(BunitContext ctx, PublishVisibility visibility)
    {
        var cut = ctx.Render<GoLiveStep>(parameters => parameters.Add(step => step.Flow, Flow(visibility)));
        cut.Find("[data-go-live-apply]").Click();
        return cut;
    }

    private static DataToPublishFlowState Flow(PublishVisibility visibility) => new()
    {
        Resource = new ResourceStaging { ResourceId = "conn-1-layer-7", Name = "Parcels" },
        ServiceSlot = ServiceName,
        EnabledProtocols = ["FeatureServer", "OgcFeatures"],
        Visibility = visibility,
        Step = FlowStep.GoLive,
    };

    private static ServiceConfigurationResult Rejected(string detail) =>
        new() { Succeeded = false, State = "Rejected", Detail = detail };

    private sealed class RecordingServiceConfiguration : IServiceConfigurationOperation
    {
        public IReadOnlyList<string>? EnabledAfterUpdate { get; init; }

        public ServiceConfigurationResult? ProtocolsResult { get; init; }

        public ServiceConfigurationResult? AccessPolicyResult { get; init; }

        public ServiceProtocolsCommand? LastProtocols { get; private set; }

        public ServiceAccessPolicyCommand? LastAccessPolicy { get; private set; }

        public Task? ProtocolsGate { get; init; }

        public async Task<ServiceConfigurationResult> UpdateProtocolsAsync(ServiceProtocolsCommand command, CancellationToken cancellationToken = default)
        {
            LastProtocols = command;
            if (ProtocolsGate is not null)
            {
                await ProtocolsGate;
            }

            return ProtocolsResult ?? Updated(EnabledAfterUpdate ?? command.EnabledProtocols);
        }

        public Task<ServiceConfigurationResult> UpdateAccessPolicyAsync(ServiceAccessPolicyCommand command, CancellationToken cancellationToken = default)
        {
            LastAccessPolicy = command;
            return Task.FromResult(AccessPolicyResult ?? Updated([]));
        }

        public Task<ServiceSettingsView> GetSettingsAsync(string serviceName, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ServiceConfigurationResult> SetLayerEnabledAsync(ServiceLayerEnableCommand command, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ServiceConfigurationResult> UpdateMapServerSettingsAsync(ServiceMapServerSettingsCommand command, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        private static ServiceConfigurationResult Updated(IReadOnlyList<string> enabledProtocols) =>
            new() { Succeeded = true, State = "Updated", ServiceName = ServiceName, EnabledProtocols = enabledProtocols };
    }
}
