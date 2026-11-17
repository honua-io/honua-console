using Bunit;
using Honua.Console.Shell.Models;
using Honua.Console.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Honua.Console.Shell.Tests;

// A bUnit context with a scripted Exports client and the JS modules the Exports pages import.
internal sealed class ExportsPageFixture : IDisposable
{
    public ExportsPageFixture()
    {
        Context.JSInterop.SetupModule("./_content/Honua.Console.Shell/console-confirm-dialog.js").Mode = JSRuntimeMode.Loose;
        Context.JSInterop.SetupModule("./_content/Honua.Console.Shell/unsaved-changes-guard.js").Mode = JSRuntimeMode.Loose;
        Clipboard = Context.JSInterop.SetupModule("./_content/Honua.Console.Shell/catalog-item-editor.js");
        Clipboard.Setup<bool>("copyText", _ => true).SetResult(true);
        Context.Services.AddSingleton(new ConsoleAgentContextRegistry());
        Context.Services.AddSingleton(new ConsoleV4Options { Enabled = true });
        Context.Services.AddSingleton<IConsoleExportsClient>(Client);
    }

    public BunitContext Context { get; } = new();

    public StubExportsClient Client { get; } = new();

    public BunitJSModuleInterop Clipboard { get; }

    public void Dispose() => Context.Dispose();
}
