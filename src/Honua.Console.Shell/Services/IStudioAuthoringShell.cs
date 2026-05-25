using Honua.Console.Shell.Models;

namespace Honua.Console.Shell.Services;

public interface IStudioAuthoringShell
{
    StudioAuthoringSession CreateInitialSession();

    StudioAuthoringSession SelectWorkflow(StudioAuthoringSession session, string workflowId);

    StudioAuthoringSession SubmitPrompt(StudioAuthoringSession session, string workflowId, string prompt);

    StudioAuthoringSession ApplyClarification(StudioAuthoringSession session, string questionId, string choiceId);

    StudioAuthoringSession TransitionPackage(StudioAuthoringSession session, StudioPackageLifecycleState state);
}
