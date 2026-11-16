using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

internal sealed class WorkflowJobEvidenceRead
{
    private string? _jobId;
    private bool _initialized;
    private long _generation;
    private Task _load = Task.CompletedTask;

    public StudioWorkflowJobEvidence? Evidence { get; private set; }

    public Task LoadAsync(IStudioWorkflowPackageClient workflows, string? jobId)
    {
        if (_initialized && string.Equals(_jobId, jobId, StringComparison.Ordinal)) return _load;
        _initialized = true;
        _jobId = jobId;
        var generation = ++_generation;
        Evidence = null;
        _load = string.IsNullOrWhiteSpace(jobId)
            ? Task.CompletedTask
            : ReadAsync(workflows, jobId, generation);
        return _load;
    }

    private async Task ReadAsync(IStudioWorkflowPackageClient workflows, string jobId, long generation)
    {
        var evidence = await workflows.GetJobEvidenceAsync(jobId);
        if (generation == _generation) Evidence = evidence;
    }
}
