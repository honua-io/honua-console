using System.Net.Http.Json;
using Honua.Console.Shell.DependencyInjection;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;
using Honua.Console.Web.Components;
using Honua.Console.Web.Runtime;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.JSInterop;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

var applicationBaseUri = new Uri(builder.HostEnvironment.BaseAddress);
using var configurationClient = new HttpClient { BaseAddress = applicationBaseUri };
var configuration = await configurationClient.GetFromJsonAsync<ConsoleRuntimeConfiguration>("config.json")
    ?? throw new InvalidOperationException("Console config.json is missing.");
var apiBaseUri = configuration.ResolveApiBaseUri(applicationBaseUri);
var session = new ConsoleBrowserSession();
var clients = new BrowserServerBoundClientFactory(apiBaseUri, applicationBaseUri, session);
builder.Services.AddSingleton(configuration);
builder.Services.AddSingleton(session);
builder.Services.AddSingleton<IConsolePreviewAddress>(new ConfiguredApiPreviewAddress(apiBaseUri));
builder.Services.AddSingleton<IHonuaServerBoundClientFactory>(clients);
builder.Services.AddSingleton(new ConsoleApiClient(clients.CreatePublicClient(apiBaseUri))
{
    UsesCookieSession = Uri.Compare(apiBaseUri, applicationBaseUri, UriComponents.SchemeAndServer,
        UriFormat.Unescaped, StringComparison.OrdinalIgnoreCase) == 0,
    SupportsPasswordSignIn = configuration.Auth.Mode == "builtin"
});
builder.Services.AddSingleton<IConsoleEnvironmentProfileStore>(new InMemoryConsoleEnvironmentProfileStore([
    new ConsoleEnvironmentProfile
    {
        Id = "configured-api",
        DisplayName = apiBaseUri.Host,
        ServerBaseUri = apiBaseUri
    }
]));
builder.Services.AddHonuaConsoleShell(new HonuaConsoleShellOptions
{
    ServerBaseUrl = apiBaseUri.AbsoluteUri,
    V4Enabled = configuration.V4Enabled
});
builder.Services.AddAuthorizationCore();
builder.Services.AddSingleton<BrowserAuthenticationStateProvider>(services => new BrowserAuthenticationStateProvider(
    services.GetRequiredService<ConsoleApiClient>(), session,
    services.GetRequiredService<IConsoleAccountSessionStore>()));

builder.Services.AddSingleton<AuthenticationStateProvider>(services => services.GetRequiredService<BrowserAuthenticationStateProvider>());
builder.Services.AddSingleton<IConsoleBrowserAuthentication>(services => services.GetRequiredService<BrowserAuthenticationStateProvider>());

builder.Services.AddSingleton<IConsoleOidcAuthentication, BrowserOidcAuthentication>();

var host = builder.Build();
await using var transport = await host.Services.GetRequiredService<IJSRuntime>()
    .InvokeAsync<IJSObjectReference>("import", "./js/console-browser-transport.mjs");
using var sessionBridge = DotNetObjectReference.Create(new ConsoleBrowserSessionBridge(session));
await transport.InvokeVoidAsync("initialize", sessionBridge, new
{
    ApiBaseUrl = apiBaseUri.AbsoluteUri,
    ApplicationBaseUrl = applicationBaseUri.AbsoluteUri
});
session.Changed += snapshot => _ = transport.InvokeVoidAsync("publishSession", snapshot);
await transport.InvokeVoidAsync("publishSession", session.Snapshot());
await host.RunAsync();
