using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

// One connection's table list as the publish pickers show it. Every load or reset starts a new
// generation, so a read that answers for an earlier selection changes nothing.
public sealed class ServiceLayerTableLoad
{
    private long _generation;

    public IReadOnlyList<ServiceLayerPublishTable> Tables { get; private set; } = [];

    public bool Loading { get; private set; }

    public string? Failure { get; private set; }

    public IReadOnlyList<OperateCapabilityState> States { get; private set; } = [];

    public bool Unavailable => Failure is not null || States.Count > 0;

    public void Reset()
    {
        _generation++;
        Tables = [];
        Loading = false;
        ClearFailure();
    }

    // No connection selected resets the list; a selected one is read.
    public Task LoadAsync(IServiceLayerPublishOperation operation, string? connectionId)
    {
        ClearFailure();
        return ReadAsync(operation, connectionId);
    }

    // A retry leaves the failure notice on screen until it answers, so its Try again button keeps focus.
    public Task RetryAsync(IServiceLayerPublishOperation operation, string? connectionId) =>
        ReadAsync(operation, connectionId);

    private async Task ReadAsync(IServiceLayerPublishOperation operation, string? connectionId)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (string.IsNullOrWhiteSpace(connectionId))
        {
            Reset();
            return;
        }

        var generation = ++_generation;
        Tables = [];
        Loading = true;
        try
        {
            var tables = await operation.ListTablesAsync(connectionId);
            if (generation != _generation) return;
            Tables = tables;
            ClearFailure();
        }
        catch (ServiceLayerTableReadException ex)
        {
            if (generation != _generation) return;
            Failure = null;
            States = [ex.State];
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or TaskCanceledException)
        {
            if (generation != _generation) return;
            Failure = ex.Message;
            States = [];
        }
        finally
        {
            if (generation == _generation) Loading = false;
        }
    }

    private void ClearFailure()
    {
        Failure = null;
        States = [];
    }
}
