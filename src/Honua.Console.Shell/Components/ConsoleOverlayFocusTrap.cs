using Honua.Console.Shell.Interop;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace Honua.Console.Shell.Components;

public sealed class ConsoleOverlayFocusTrap : ComponentBase, IHandleAfterRender, IAsyncDisposable
{
    public const string ModulePath = "./_content/Honua.Console.Shell/console-overlay-focus.js";

    private readonly CancellationTokenSource _importLifetime = new();
    private readonly RetryingModuleImport _moduleImport = new(ModuleImportScriptError.Retry);
    private readonly string _overlayKey = Guid.NewGuid().ToString("N");
    private ElementReference _element;
    private IJSObjectReference? _module;
    private DotNetObjectReference<ConsoleOverlayFocusTrap>? _dotNetRef;
    private bool _importing;
    private bool _activated;

    [Inject] private IServiceProvider Services { get; set; } = default!;

    [Parameter] public bool CanDismiss { get; set; } = true;
    [Parameter] public EventCallback OnDismiss { get; set; }
    [Parameter] public RenderFragment? ChildContent { get; set; }
    [Parameter(CaptureUnmatchedValues = true)] public IReadOnlyDictionary<string, object>? AdditionalAttributes { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "div");
        builder.AddMultipleAttributes(1, AdditionalAttributes);
        builder.AddAttribute(2, "tabindex", -1);
        builder.AddAttribute(3, "onkeydown", EventCallback.Factory.Create<KeyboardEventArgs>(this, HandleKeyDownAsync));
        builder.AddElementReferenceCapture(4, element => _element = element);
        builder.AddContent(5, ChildContent);
        builder.CloseElement();
    }

    async Task IHandleAfterRender.OnAfterRenderAsync()
    {
        if (_activated || _importing) return;
        if (Services.GetService<IJSRuntime>() is not { } runtime) return;

        await EnsureModuleLoadedAsync(runtime);
        if (_module is null || _activated) return;

        await ActivateOverlayAsync();
    }

    private async Task EnsureModuleLoadedAsync(IJSRuntime runtime)
    {
        if (_module is not null) return;
        _importing = true;
        try
        {
            _module = await _moduleImport.LoadAsync(runtime, ModulePath, _importLifetime.Token);
        }
        catch (Exception ex) when (ex is JSDisconnectedException or OperationCanceledException)
        {
        }
        finally
        {
            _importing = false;
        }
    }

    private async Task ActivateOverlayAsync()
    {
        if (_module is null) return;
        _activated = true;
        _dotNetRef ??= DotNetObjectReference.Create(this);
        try
        {
            await _module.InvokeVoidAsync("activate", _element, _overlayKey, _dotNetRef);
        }
        catch (Exception ex) when (ex is JSDisconnectedException or JSException or InvalidOperationException)
        {
            _activated = false;
        }
    }

    private Task HandleKeyDownAsync(KeyboardEventArgs args) =>
        args.Key == "Escape" && CanDismiss ? OnDismiss.InvokeAsync() : Task.CompletedTask;

    [JSInvokable]
    public Task NotifyEscapeAsync() => CanDismiss ? OnDismiss.InvokeAsync() : Task.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        _importLifetime.Cancel();
        if (_module is not null)
        {
            await DeactivateAndDisposeModuleAsync();
        }
        _dotNetRef?.Dispose();
        _importLifetime.Dispose();
    }

    private async Task DeactivateAndDisposeModuleAsync()
    {
        try
        {
            if (_activated)
            {
                await _module!.InvokeVoidAsync("deactivate", _overlayKey);
            }
            await _module!.DisposeAsync();
        }
        catch (Exception ex) when (ex is JSDisconnectedException or JSException or InvalidOperationException)
        {
        }
        finally
        {
            _activated = false;
            _module = null;
        }
    }
}
