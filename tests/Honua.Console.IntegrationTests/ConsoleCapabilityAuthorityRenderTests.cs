using System.Security.Claims;
using Bunit;
using Honua.Console.Shell.Components;
using Honua.Console.Shell.DependencyInjection;
using Honua.Console.Shell.Layout;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.IntegrationTests;

public sealed class ConsoleCapabilityAuthorityRenderTests
{
    [Fact]
    public async Task SignInAdvertisesCatalogAndSignOutRevokesItWithoutNavigation()
    {
        await using var context = CreateContext();
        var authentication = context.Services.GetRequiredService<MutableAuthentication>();
        var shell = context.Render<ConsoleLayout>();
        var gate = context.Render<ConsoleCapabilityGate>(parameters => parameters
            .Add(component => component.Capability, ConsoleCapabilityKeys.PersistentCatalog)
            .AddChildContent("<p id='authorized-content'>Authorized content</p>"));
        Assert.Empty(CatalogLinks(shell));
        Assert.Empty(gate.FindAll("#authorized-content"));

        authentication.SignIn();
        shell.WaitForAssertion(() => Assert.Equal(2, CatalogLinks(shell).Count));
        gate.WaitForAssertion(() => Assert.Single(gate.FindAll("#authorized-content")));
        authentication.SignOut();
        shell.WaitForAssertion(() => Assert.Empty(CatalogLinks(shell)));
        gate.WaitForAssertion(() => Assert.Empty(gate.FindAll("#authorized-content")));
    }

    [Fact]
    public async Task EnvironmentChangeStillWaitsForSlowRestoredAuthentication()
    {
        await using var context = CreateContext();
        var profiles = new InMemoryConsoleEnvironmentProfileStore([
            new ConsoleEnvironmentProfile { Id = "first", DisplayName = "First", ServerBaseUri = new Uri("https://first.test/") },
            new ConsoleEnvironmentProfile { Id = "second", DisplayName = "Second", ServerBaseUri = new Uri("https://second.test/") }
        ]);
        context.Services.AddSingleton<IConsoleEnvironmentProfileStore>(profiles);
        var authentication = context.Services.GetRequiredService<MutableAuthentication>();
        var restored = authentication.DeferInitialState();
        var shell = context.Render<ConsoleLayout>();
        var registry = context.Services.GetRequiredService<AuthorityRegistry>();
        Assert.Equal(0, registry.Reads);
        Assert.Empty(CatalogLinks(shell));

        await Task.Delay(TimeSpan.FromSeconds(5.1));
        await shell.InvokeAsync(() => profiles.ActivateProfileAsync("second"));
        Assert.Equal(0, registry.Reads);
        restored.SetResult(authentication.AuthenticatedState());
        shell.WaitForAssertion(() => Assert.Equal(2, CatalogLinks(shell).Count));
        Assert.Equal(1, registry.Reads);
    }

    [Fact]
    public async Task EnvironmentChangesRefreshAndDisposedShellStopsListening()
    {
        await using var context = CreateContext();
        var profiles = new InMemoryConsoleEnvironmentProfileStore([
            new ConsoleEnvironmentProfile { Id = "first", DisplayName = "First", ServerBaseUri = new Uri("https://first.test/") },
            new ConsoleEnvironmentProfile { Id = "second", DisplayName = "Second", ServerBaseUri = new Uri("https://second.test/") }
        ]);
        context.Services.AddSingleton<IConsoleEnvironmentProfileStore>(profiles);
        var authentication = context.Services.GetRequiredService<MutableAuthentication>();
        authentication.SignIn();
        var shell = context.Render<ConsoleLayout>();
        shell.WaitForAssertion(() => Assert.Equal(2, CatalogLinks(shell).Count));
        var registry = context.Services.GetRequiredService<AuthorityRegistry>();
        registry.Available = false;

        await shell.InvokeAsync(() => profiles.ActivateProfileAsync("second"));
        shell.WaitForAssertion(() => Assert.Empty(CatalogLinks(shell)));
        Assert.Equal(2, registry.Reads);
        await context.DisposeComponentsAsync();
        authentication.SignOut();
        await profiles.ActivateProfileAsync("first");
        Assert.Equal(2, registry.Reads);
    }

    private static System.Collections.Generic.IReadOnlyList<AngleSharp.Dom.IElement> CatalogLinks(IRenderedComponent<ConsoleLayout> shell) =>
        shell.FindAll(".console-topnav a[href='/catalog'], .console-topnav a[href='/share/public']");

    private static BunitContext CreateContext()
    {
        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.AddAuthorization();
        context.Services.AddHonuaConsoleShell(new HonuaConsoleShellOptions { CatalogAdminEnabled = true });
        context.Services.AddSingleton<MutableAuthentication>();
        context.Services.AddSingleton<AuthenticationStateProvider>(services => services.GetRequiredService<MutableAuthentication>());
        context.Services.AddSingleton<AuthorityRegistry>();
        context.Services.AddSingleton<IConsoleCapabilityManifest>(services => new ManifestBackedConsoleCapabilityManifest(
            services.GetRequiredService<AuthorityRegistry>()));
        return context;
    }

    private sealed class MutableAuthentication : AuthenticationStateProvider
    {
        private Task<AuthenticationState> _state = Task.FromResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity())));
        public bool Authenticated { get; private set; }
        public override Task<AuthenticationState> GetAuthenticationStateAsync() => _state;
        public AuthenticationState AuthenticatedState()
        {
            Authenticated = true;
            return new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity([
                new Claim(ClaimTypes.NameIdentifier, "operator"), new Claim(ClaimTypes.Name, "Operator")
            ], "test")));
        }

        public TaskCompletionSource<AuthenticationState> DeferInitialState()
        {
            var completion = new TaskCompletionSource<AuthenticationState>(TaskCreationOptions.RunContinuationsAsynchronously);
            _state = completion.Task;
            return completion;
        }

        public void SignIn()
        {
            _state = Task.FromResult(AuthenticatedState());
            NotifyAuthenticationStateChanged(_state);
        }

        public void SignOut()
        {
            Authenticated = false;
            _state = Task.FromResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity())));
            NotifyAuthenticationStateChanged(_state);
        }
    }

    private sealed class AuthorityRegistry(MutableAuthentication authentication) : ICapabilityRegistryClient
    {
        public int Reads { get; private set; }
        public bool Available { get; set; } = true;
        public Task<CapabilityRegistrySnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
        {
            Reads++;
            return Task.FromResult(new CapabilityRegistrySnapshot
            {
                Bound = authentication.Authenticated,
                Descriptors = [new CapabilityDescriptor("console.content.persistent", Available, true)]
            });
        }
    }
}
