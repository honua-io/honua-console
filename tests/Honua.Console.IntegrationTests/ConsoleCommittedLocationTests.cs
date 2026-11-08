using System.Security.Claims;
using Bunit;
using Honua.Console.Contracts;
using Honua.Console.Shell.Layout;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

public sealed class ConsoleCommittedLocationTests
{
    private static ConsoleEnvironmentProfile Profile => new()
    {
        Id = "one",
        ServerBaseUri = new Uri("https://one.example.test"),
        Account = new ConsoleAccountBinding { AccountId = "operator-one" }
    };

    [Fact]
    public async Task CapabilityRefreshAndQueryNavigationKeepTheAuthorityRevision()
    {
        using var context = CreateContext();
        var capabilities = new CapabilitySignals();
        context.Services.AddSingleton<IConsoleCapabilityManifest>(capabilities);
        context.Services.AddSingleton<IConsoleEnvironmentProfileStore>(new InMemoryConsoleEnvironmentProfileStore([Profile]));
        var marker = context.Render<ConsoleCommittedLocation>();
        await marker.InvokeAsync(() => capabilities.RefreshAsync());
        context.Services.GetRequiredService<NavigationManager>().NavigateTo("/?view=one#details");
        Assert.Equal("0", Revision(marker));
        Assert.Equal("/", marker.Find("span").GetAttribute("data-console-location"));
        Assert.False(marker.Find("span").HasAttribute("data-console-location-revision"));
    }

    [Fact]
    public async Task ProfileActivationOnTheSameUrlChangesTheRevisionExactlyOnce()
    {
        using var context = CreateContext();
        var profiles = new InMemoryConsoleEnvironmentProfileStore([Profile, Profile with { Id = "two" }], activeProfileId: "one");
        context.Services.AddSingleton<IConsoleEnvironmentProfileStore>(profiles);
        var marker = context.Render<ConsoleCommittedLocation>();
        await marker.InvokeAsync(() => profiles.ActivateProfileAsync("two"));
        marker.WaitForAssertion(() => Assert.Equal("1", Revision(marker)));
        await marker.InvokeAsync(() => profiles.ActivateProfileAsync("two"));
        Assert.Equal("1", Revision(marker));
        Assert.Equal("/", marker.Find("span").GetAttribute("data-console-location"));
        Assert.DoesNotContain("operator-one", marker.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ActiveServerAndAccountUpdatesEachInvalidateTheRevision()
    {
        using var context = CreateContext();
        var profiles = new InMemoryConsoleEnvironmentProfileStore([Profile]);
        context.Services.AddSingleton<IConsoleEnvironmentProfileStore>(profiles);
        var marker = context.Render<ConsoleCommittedLocation>();
        await marker.InvokeAsync(() => profiles.UpsertProfileAsync(Profile with { DisplayName = "Renamed" }));
        Assert.Equal("0", Revision(marker));
        var updated = Profile with { ServerBaseUri = new Uri("https://two.example.test") };
        await marker.InvokeAsync(() => profiles.UpsertProfileAsync(updated));
        marker.WaitForAssertion(() => Assert.Equal("1", Revision(marker)));
        await marker.InvokeAsync(() => profiles.UpsertProfileAsync(updated with
        {
            Account = new ConsoleAccountBinding { AccountId = "operator-two" }
        }));
        marker.WaitForAssertion(() => Assert.Equal("2", Revision(marker)));
    }

    [Fact]
    public async Task AuthenticationSignalRechecksTheProfileWithoutACapabilityRefresh()
    {
        using var context = CreateContext();
        var profiles = new ProfileReads { Current = Profile };
        var authentication = new AuthenticationSignals();
        context.Services.AddSingleton<IConsoleEnvironmentProfileStore>(profiles);
        context.Services.AddSingleton<AuthenticationStateProvider>(authentication);
        var marker = context.Render<ConsoleCommittedLocation>();
        profiles.Current = Profile with { Account = new ConsoleAccountBinding { AccountId = "operator-two" } };
        await marker.InvokeAsync(authentication.Publish);
        marker.WaitForAssertion(() => Assert.Equal("1", Revision(marker)));
        await marker.InvokeAsync(authentication.Publish);
        Assert.Equal("1", Revision(marker));
    }

    [Fact]
    public async Task DelayedProfileReadCannotOverwriteANewerAuthority()
    {
        using var context = CreateContext();
        var profiles = new ProfileReads { Current = Profile };
        context.Services.AddSingleton<IConsoleEnvironmentProfileStore>(profiles);
        var marker = context.Render<ConsoleCommittedLocation>();
        var delayed = new TaskCompletionSource<ConsoleEnvironmentProfile?>();
        profiles.Pending = delayed.Task;
        await marker.InvokeAsync(profiles.Publish);
        profiles.Pending = null;
        profiles.Current = Profile with { Id = "two" };
        await marker.InvokeAsync(profiles.Publish);
        marker.WaitForAssertion(() => Assert.Equal("1", Revision(marker)));
        await marker.InvokeAsync(() => delayed.SetResult(Profile));
        Assert.Equal("1", Revision(marker));
    }

    [Fact]
    public void DisposalDetachesProfileAndAuthenticationSignals()
    {
        using var context = CreateContext();
        var profiles = new ProfileReads { Current = Profile };
        var authentication = new AuthenticationSignals();
        context.Services.AddSingleton<IConsoleEnvironmentProfileStore>(profiles);
        context.Services.AddSingleton<AuthenticationStateProvider>(authentication);
        var marker = context.Render<ConsoleCommittedLocation>();
        context.Dispose();
        var reads = profiles.Reads;
        profiles.Publish();
        authentication.Publish();
        Assert.Equal(reads, profiles.Reads);
    }

    [Fact]
    public async Task HostScopeIsPublishedWithoutAConversationAndFollowsAuthenticationIndependently()
    {
        using var context = CreateContext();
        var bindings = new BindingReads { Current = new string('a', 64) };
        var authentication = new AuthenticationSignals();
        context.Services.AddSingleton<IConsoleEnvironmentProfileStore>(new InMemoryConsoleEnvironmentProfileStore([Profile]));
        context.Services.AddSingleton<IConsoleAuthorityBindingValidator>(bindings);
        context.Services.AddSingleton<AuthenticationStateProvider>(authentication);
        var marker = context.Render<ConsoleCommittedLocation>();
        marker.WaitForAssertion(() => Assert.Equal(bindings.Current, marker.Find("span").GetAttribute("data-console-recent-scope")));
        Assert.True(long.Parse(marker.Find("span").GetAttribute("data-console-recent-until")!) > DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        bindings.Current = new string('b', 64);
        await marker.InvokeAsync(authentication.Publish);
        marker.WaitForAssertion(() => Assert.Equal(bindings.Current, marker.Find("span").GetAttribute("data-console-recent-scope")));
        Assert.Equal("0", Revision(marker));
        Assert.DoesNotContain("operator-one", marker.Markup);
    }

    [Fact]
    public async Task FailedRecentScopeReadDoesNotMasqueradeAsADrawerAuthorityChange()
    {
        using var context = CreateContext();
        var bindings = new BindingReads { Current = new string('a', 64) };
        var profiles = new ProfileReads { Current = Profile };
        context.Services.AddSingleton<IConsoleEnvironmentProfileStore>(profiles);
        context.Services.AddSingleton<IConsoleAuthorityBindingValidator>(bindings);
        var marker = context.Render<ConsoleCommittedLocation>();
        bindings.Failure = new InvalidOperationException("Unavailable");
        await marker.InvokeAsync(profiles.Publish);
        marker.WaitForAssertion(() => Assert.False(marker.Find("span").HasAttribute("data-console-recent-scope")));
        Assert.Equal("0", Revision(marker));
    }

    [Fact]
    public async Task DelayedScopeReadCannotRestoreAPreviousOperatorsHistory()
    {
        using var context = CreateContext();
        var bindings = new BindingReads { Current = new string('a', 64) };
        var profiles = new ProfileReads { Current = Profile };
        context.Services.AddSingleton<IConsoleEnvironmentProfileStore>(profiles);
        context.Services.AddSingleton<IConsoleAuthorityBindingValidator>(bindings);
        var marker = context.Render<ConsoleCommittedLocation>();
        var delayed = new TaskCompletionSource<string?>();
        bindings.Pending = delayed.Task;
        await marker.InvokeAsync(profiles.Publish);
        Assert.False(marker.Find("span").HasAttribute("data-console-recent-scope"));
        bindings.Pending = null;
        bindings.Current = new string('b', 64);
        await marker.InvokeAsync(profiles.Publish);
        marker.WaitForAssertion(() => Assert.Equal(bindings.Current, marker.Find("span").GetAttribute("data-console-recent-scope")));
        await marker.InvokeAsync(() => delayed.SetResult(new string('a', 64)));
        Assert.Equal(bindings.Current, marker.Find("span").GetAttribute("data-console-recent-scope"));
    }

    private sealed class BindingReads : IConsoleAuthorityBindingValidator
    {
        public string? Current { get; set; }
        public Task<string?>? Pending { get; set; }
        public Exception? Failure { get; set; }
        public Task<string?> CaptureAsync(CancellationToken token = default) =>
            Failure is not null ? Task.FromException<string?>(Failure) : Pending ?? Task.FromResult(Current);
        public Task<bool> IsCurrentAsync(string binding, CancellationToken token = default) => Task.FromResult(binding == Current);
    }

    private static string? Revision(IRenderedComponent<ConsoleCommittedLocation> marker) =>
        marker.Find("span").GetAttribute("data-console-authority-revision");

    private static BunitContext CreateContext()
    {
        var context = new BunitContext();
        context.Services.AddSingleton(new CatalogAdminOptions());
        return context;
    }

    private sealed class CapabilitySignals : IConsoleCapabilityManifest
    {
        public event Action? Changed;
        public bool IsAdvertised(string key) => false;
        public Task RefreshAsync(CancellationToken cancellationToken = default)
        {
            Changed?.Invoke();
            Changed?.Invoke();
            return Task.CompletedTask;
        }
    }

    private sealed class AuthenticationSignals : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync() =>
            Task.FromResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity())));
        public void Publish() => NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
    }

    private sealed class ProfileReads : IConsoleEnvironmentProfileStore
    {
        public ConsoleEnvironmentProfile? Current { get; set; }
        public Task<ConsoleEnvironmentProfile?>? Pending { get; set; }
        public int Reads { get; private set; }
        public event Action? ActiveProfileChanged;
        public void Publish() => ActiveProfileChanged?.Invoke();
        public Task<ConsoleEnvironmentProfile?> GetActiveProfileAsync(CancellationToken cancellationToken = default)
        {
            Reads++;
            return Pending ?? Task.FromResult(Current);
        }
        public Task<IReadOnlyList<ConsoleEnvironmentProfile>> ListProfilesAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ConsoleEnvironmentProfile?> GetProfileAsync(string id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task UpsertProfileAsync(ConsoleEnvironmentProfile profile, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task ActivateProfileAsync(string id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ConsoleEnvironmentState?> GetStateAsync(string id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SaveStateAsync(ConsoleEnvironmentState state, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
