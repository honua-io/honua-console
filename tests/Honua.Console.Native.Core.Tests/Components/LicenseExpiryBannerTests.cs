using Honua.Console.Contracts;
using Honua.Console.Shell.Components;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Text;
using System.Text.Json;
using Bunit;

namespace Honua.Console.Native.Core.Tests.Components;

public sealed class LicenseExpiryBannerTests : ConsoleComponentTestBase
{
    [Theory]
    [InlineData("Pro", 30, 30)]
    [InlineData("Enterprise", 14, 14)]
    [InlineData("Pro", 7, 7)]
    [InlineData("Enterprise", 1, 1)]
    [InlineData("Pro", 13, 14)]
    [InlineData("Enterprise", 0, 0)]
    public void PaidExpiry_RendersPersistentThresholdWarning(string edition, int days, int threshold)
    {
        Register(edition, days);
        var component = Render<LicenseExpiryBanner>();
        component.WaitForAssertion(() =>
        {
            Assert.Equal(threshold.ToString(), component.Find("[role=alert]").GetAttribute("data-license-warning-days"));
            Assert.Contains("backup/export before expiry", component.Markup);
            Assert.Contains("Reads and exports stop", component.Markup);
        });
    }

    [Theory]
    [InlineData("Community", 1)]
    [InlineData("Community", -1)]
    [InlineData("Pro", 31)]
    public void CommunityOrDistantExpiry_HasNoWarning(string edition, int days)
    {
        Register(edition, days);
        var component = Render<LicenseExpiryBanner>();
        Assert.Empty(component.FindAll("[role=alert]"));
    }

    [Fact]
    public void Renewal_NextMinuteRemovesWarning()
    {
        var clock = new MinuteClock();
        Services.AddSingleton<TimeProvider>(clock);
        var handler = Register("Pro", 1);
        var component = Render<LicenseExpiryBanner>();
        component.WaitForAssertion(() => Assert.Single(component.FindAll("[role=alert]")));
        handler.Response = Response("Pro", 60);
        clock.Tick();
        component.WaitForAssertion(() => Assert.Empty(component.FindAll("[role=alert]")));
    }

    [Theory]
    [InlineData("unreachable")]
    [InlineData("non-success")]
    [InlineData("malformed")]
    [InlineData("missing-data")]
    public void FailedRefresh_RetainsWarningUntilSuccessfulRenewal(string failure)
    {
        var clock = new MinuteClock();
        Services.AddSingleton<TimeProvider>(clock);
        var handler = Register("Pro", 1);
        var component = Render<LicenseExpiryBanner>();
        component.WaitForAssertion(() => Assert.Equal("1",
            component.Find("[role=alert]").GetAttribute("data-license-warning-days")));

        handler.Failure = failure;
        clock.UtcNow = clock.UtcNow.AddDays(2);
        clock.Tick();
        // A new threshold proves the failed poll has rendered, rather than inspecting the old render.
        component.WaitForAssertion(() =>
        {
            Assert.Equal("0", component.Find("[role=alert]").GetAttribute("data-license-warning-days"));
            Assert.Contains("Pro license expired", component.Markup);
        });

        handler.Failure = null;
        handler.Response = Response("Pro", 60);
        clock.Tick();
        component.WaitForAssertion(() => Assert.Empty(component.FindAll("[role=alert]")));
    }

    [Fact]
    public async Task Disposal_CancelsPendingStatusRequest()
    {
        var handler = new WaitingHandler();
        Services.AddSingleton<IHonuaAdminOperateClient>(_ => new HonuaAdminOperateHttpClient(
            new HttpClient(handler),
            new HonuaAdminOperateClientOptions(new Uri("https://synthetic.example"))));
        Render<LicenseExpiryBanner>();
        await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await DisposeAsync();
        Assert.True(handler.WasCancelled);
    }

    private ReplyHandler Register(string edition, int days)
    {
        var handler = new ReplyHandler(Response(edition, days));
        Services.AddSingleton<IHonuaAdminOperateClient>(_ => new HonuaAdminOperateHttpClient(
            new HttpClient(handler),
            new HonuaAdminOperateClientOptions(new Uri("https://synthetic.example"))));
        return handler;
    }

    private static string Response(string edition, int days)
    {
        return JsonSerializer.Serialize(new
        {
            success = true,
            data = new
            {
                edition,
                expiresAt = DateTimeOffset.UtcNow.AddDays(days).AddMinutes(-1),
                isValid = days > 0
            }
        });
    }

    private sealed class ReplyHandler(string response) : HttpMessageHandler
    {
        public string Response { get; set; } = response;
        public string? Failure { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Failure == "unreachable")
            {
                throw new HttpRequestException("Synthetic server outage.");
            }
            var payload = Failure switch
            {
                "malformed" => "{",
                "missing-data" => "{\"success\":true,\"data\":null}",
                _ => Response
            };
            return Task.FromResult(new HttpResponseMessage(
                Failure == "non-success" ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class MinuteClock : TimeProvider
    {
        private TimerCallback? _callback;
        private object? _state;
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UtcNow;

        public override DateTimeOffset GetUtcNow() => UtcNow;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            Assert.Equal(TimeSpan.FromMinutes(1), dueTime);
            Assert.Equal(TimeSpan.FromMinutes(1), period);
            _callback = callback;
            _state = state;
            return new ManualTimer();
        }

        public void Tick() => _callback!(_state);

        private sealed class ManualTimer : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;
            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private sealed class WaitingHandler : HttpMessageHandler
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool WasCancelled { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Started.SetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                throw new InvalidOperationException("The request must be cancelled by disposal.");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                WasCancelled = true;
                throw;
            }
        }
    }
}
