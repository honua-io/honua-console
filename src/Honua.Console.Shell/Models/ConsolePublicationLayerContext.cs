namespace Honua.Console.Shell.Models;

public sealed record ConsolePublicationLayerSelection(string ResourceId, string ServiceName, string LayerName);

public sealed class ConsolePublicationLayerContext
{
    private string _path = "";
    private int _readRevision;
    public int Revision { get; private set; }
    public event Action? Changed;
    public ConsolePublicationLayerSelection? Selection { get; private set; }

    public readonly record struct ReadAttempt(int Revision, string ResourceId);

    public ReadAttempt BeginRead(string resourceId) => new(++_readRevision, resourceId);

    public void SetPath(string path)
    {
        if (_path == path) return;
        _path = path;
        Invalidate();
    }

    public void Invalidate()
    {
        _readRevision++;
        Revision++;
        Selection = null;
        Changed?.Invoke();
    }

    public void CompleteRead(ReadAttempt attempt, IReadOnlyList<OperateLayerExposure> exposures)
    {
        var exposure = exposures.FirstOrDefault(candidate =>
            string.Equals(candidate.Layer.CanonicalResourceId, attempt.ResourceId, StringComparison.OrdinalIgnoreCase)
            || candidate.Layer.LayerId.ToString(System.Globalization.CultureInfo.InvariantCulture) == attempt.ResourceId);
        Complete(attempt, exposure is null ? null : new(
            exposure.Layer.CanonicalResourceId, exposure.Service.Name, exposure.Layer.Name));
    }

    public void CompleteStyleRead(ReadAttempt attempt, OperateLayerStyleOverrideView view)
    {
        var slot = view.BindingState is null && view.ResourceId == attempt.ResourceId ? view.Slots.FirstOrDefault() : null;
        Complete(attempt, slot is null ? null : new(view.ResourceId, slot.ServiceName, slot.ServiceDisplayName));
    }

    private void Complete(ReadAttempt attempt, ConsolePublicationLayerSelection? selection)
    {
        if (attempt.Revision != _readRevision || _path != $"/operate/layers/{Uri.EscapeDataString(attempt.ResourceId)}") return;
        if (Selection == selection) return;
        Revision++;
        Selection = selection;
        Changed?.Invoke();
    }
}
