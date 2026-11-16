using Bunit;
using Honua.Console.Shell.Components;
using Honua.Console.Shell.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace Honua.Console.IntegrationTests;

public sealed class ConsoleConnectionNoticeRenderTests
{
    [Theory]
    [InlineData(401, "Sign in to read this environment", "/auth/login")]
    [InlineData(403, "Access to these objects was denied", "/environments")]
    public void CanonicalMissingPermissionRetainsActualHttpMeaning(int status, string heading, string href)
    {
        using var context = new BunitContext();
        var page = context.Render<ConsoleConnectionNotice>(parameters => parameters.Add(value => value.States,
            new[] { new OperateCapabilityState("Resources", "Missing permission", "contract", "Request denied") { StatusCode = status } }));
        Assert.Equal(heading, page.Find("h2").TextContent);
        Assert.Equal(href, page.Find("a").GetAttribute("href"));
    }

    [Theory]
    [InlineData("Missing binding", "Connect an environment to continue", "Connect environment", "/environments")]
    [InlineData("Unauthorized", "Sign in to read this environment", "Sign in", "/auth/login")]
    [InlineData("Forbidden", "Access to these objects was denied", "Choose environment", "/environments")]
    [InlineData("Unsupported", "This feature is unavailable on this deployment", "Review environment", "/environments")]
    [InlineData("Unavailable", "Could not read this environment", "Review environment", "/environments")]
    public void CapabilityFailure_HasRelevantPrimaryStatusAndAction(string state, string heading, string action, string href)
    {
        using var context = new BunitContext();
        var component = context.Render<ConsoleConnectionNotice>(parameters => parameters
            .Add(value => value.States, new[] { new OperateCapabilityState("Resources", state, "private-config-reference", "Detailed failure") }));
        Assert.Equal(heading, component.Find("h2").TextContent);
        Assert.Equal(action, component.Find("a").TextContent);
        Assert.Equal(href, component.Find("a").GetAttribute("href"));
        Assert.Null(component.Find("details").GetAttribute("open"));
        Assert.DoesNotContain("private-config-reference", component.Find("h2").TextContent);
    }

    [Fact]
    public void BusyRetryStaysFocusableAndIgnoresClicks()
    {
        using var context = new BunitContext();
        var calls = 0;
        var component = context.Render<ConsoleConnectionNotice>(parameters => parameters
            .Add(value => value.Error, "Read failed")
            .Add(value => value.Busy, true)
            .Add(value => value.OnRetry, () => calls++));
        var button = component.Find("button");
        Assert.Equal("true", button.GetAttribute("aria-disabled"));
        Assert.False(button.HasAttribute("disabled"));
        Assert.Equal("Reading…", button.TextContent);
        button.Click();
        Assert.Equal(0, calls);
    }

    [Fact]
    public void RepeatedFailedRetryIsAnnouncedWithChangingText()
    {
        using var context = new BunitContext();
        var calls = 0;
        var component = context.Render<ConsoleConnectionNotice>(parameters => parameters
            .Add(value => value.Error, "Read failed")
            .Add(value => value.OnRetry, () => calls++));
        Assert.Null(component.Find("button").GetAttribute("aria-disabled"));
        Assert.Equal("", component.Find("[role=status]").TextContent);

        component.Find("button").Click();
        Assert.Equal("Attempt 2 did not complete. Could not read this environment.", component.Find("[role=status]").TextContent);
        component.Find("button").Click();
        Assert.Equal("Attempt 3 did not complete. Could not read this environment.", component.Find("[role=status]").TextContent);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task RetryFromClearingParentKeepsNoticeAndAnnouncesRepeatFailure()
    {
        using var context = new BunitContext();
        var host = context.Render<ClearingRetryHost>();
        host.Find("button").Click();

        // The parent cleared its error while the read runs; the notice and its button must stay.
        Assert.Single(host.FindAll("[data-connection-notice]"));
        var busyButton = host.Find("button");
        Assert.Equal("true", busyButton.GetAttribute("aria-disabled"));
        Assert.Equal("Reading…", busyButton.TextContent);

        await host.InvokeAsync(() => host.Instance.Complete(succeeded: false));
        host.WaitForAssertion(() => Assert.Equal(
            "Attempt 2 did not complete. Could not read this environment.", host.Find("[role=status]").TextContent));
        Assert.Null(host.Find("button").GetAttribute("aria-disabled"));

        host.Find("button").Click();
        await host.InvokeAsync(() => host.Instance.Complete(succeeded: true));
        host.WaitForAssertion(() => Assert.Empty(host.FindAll("[data-connection-notice]")));
        Assert.Equal(2, host.Instance.Calls);
    }

    private sealed class ClearingRetryHost : ComponentBase
    {
        private TaskCompletionSource<bool>? _pending;
        private string? _error = "Read failed";
        private bool _loading;

        public int Calls { get; private set; }

        public void Complete(bool succeeded) => _pending!.SetResult(succeeded);

        private async Task LoadAsync()
        {
            Calls++;
            _loading = true;
            _error = null;
            _pending = new TaskCompletionSource<bool>();
            var succeeded = await _pending.Task;
            _error = succeeded ? null : "Read failed again";
            _loading = false;
        }

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<ConsoleConnectionNotice>(0);
            builder.AddAttribute(1, nameof(ConsoleConnectionNotice.Error), _error);
            builder.AddAttribute(2, nameof(ConsoleConnectionNotice.Busy), _loading);
            builder.AddAttribute(3, nameof(ConsoleConnectionNotice.OnRetry), EventCallback.Factory.Create(this, LoadAsync));
            builder.CloseComponent();
        }
    }
}
