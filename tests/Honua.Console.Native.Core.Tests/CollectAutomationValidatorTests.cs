using Honua.Console.Shell.Models;
using Honua.Console.Shell.Validation;

namespace Honua.Console.Native.Core.Tests;

/// <summary>
/// Docker-free unit coverage for <see cref="CollectAutomationValidator"/>, the client validator for the
/// Collect automation editor (honua-console#219). Each console-owned rule (name + bound form, cascade-depth
/// bounds, at-least-one rule/action, supported trigger + field-change naming, supported action kind + the
/// target a kind requires) is proven in its pass and fail state, keyed by <see cref="CollectAutomationFieldKeys"/>.
/// </summary>
public sealed class CollectAutomationValidatorTests
{
    private static IReadOnlyList<ConsoleFieldError> Evaluate(CollectAutomationDraft draft) =>
        CollectAutomationValidator.Instance.Evaluate(draft);

    /// <summary>A minimal valid automation: named, bound to a form, in-bounds cascade depth, one rule + action.</summary>
    private static CollectAutomationDraft Valid()
    {
        return new CollectAutomationDraft
        {
            DraftId = "draft-1",
            Name = "Permit intake",
            FormId = "form-permit-intake",
            MaxCascadeDepth = 8,
            Rules =
            [
                new CollectAutomationRule
                {
                    Id = "rule-1",
                    Name = "Compute fee",
                    Trigger = CollectAutomationContractValues.TriggerFieldChange,
                    TriggerField = "permit_type",
                    Actions =
                    [
                        new CollectAutomationAction
                        {
                            Id = "action-1",
                            Kind = CollectAutomationContractValues.ActionCompute,
                            Target = "fee_amount",
                            Expression = "feeSchedule(permit_type)"
                        }
                    ]
                }
            ]
        };
    }

    [Fact]
    public void ValidAutomation_ProducesNoErrors() => Assert.Empty(Evaluate(Valid()));

    [Fact]
    public void MissingName_BlocksOnName()
    {
        var draft = Valid();
        draft.Name = "   ";

        var error = Assert.Single(Evaluate(draft), e => e.FieldKey == CollectAutomationFieldKeys.Name);
        Assert.Equal(ConsoleValidationSeverity.Blocker, error.Severity);
    }

    [Fact]
    public void MissingFormId_BlocksOnFormId()
    {
        var draft = Valid();
        draft.FormId = string.Empty;

        var error = Assert.Single(Evaluate(draft), e => e.FieldKey == CollectAutomationFieldKeys.FormId);
        Assert.Equal(ConsoleValidationSeverity.Blocker, error.Severity);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(33)]
    public void CascadeDepthOutOfRange_ErrorsOnDepth(int depth)
    {
        var draft = Valid();
        draft.MaxCascadeDepth = depth;

        Assert.Contains(Evaluate(draft), e =>
            e.FieldKey == CollectAutomationFieldKeys.MaxCascadeDepth && e.Code == "automation.maxCascadeDepth.range");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(32)]
    public void CascadeDepthAtBounds_ProducesNoDepthError(int depth)
    {
        var draft = Valid();
        draft.MaxCascadeDepth = depth;

        Assert.DoesNotContain(Evaluate(draft), e => e.FieldKey == CollectAutomationFieldKeys.MaxCascadeDepth);
    }

    [Fact]
    public void NoRules_BlocksOnRules()
    {
        var draft = Valid();
        draft.Rules.Clear();

        var error = Assert.Single(Evaluate(draft), e =>
            e.FieldKey == CollectAutomationFieldKeys.Rules && e.Code == "automation.rules.empty");
        Assert.Equal(ConsoleValidationSeverity.Blocker, error.Severity);
    }

    [Fact]
    public void UnsupportedTrigger_ErrorsOnRules()
    {
        var draft = Valid();
        draft.Rules[0].Trigger = "on-a-whim";

        Assert.Contains(Evaluate(draft), e =>
            e.FieldKey == CollectAutomationFieldKeys.Rules && e.Code == "automation.rule.trigger.unsupported");
    }

    [Fact]
    public void FieldChangeTriggerWithoutField_ErrorsOnTriggerField()
    {
        var draft = Valid();
        draft.Rules[0].Trigger = CollectAutomationContractValues.TriggerFieldChange;
        draft.Rules[0].TriggerField = string.Empty;

        Assert.Contains(Evaluate(draft), e =>
            e.FieldKey == CollectAutomationFieldKeys.RuleTriggerField(0)
            && e.Code == "automation.rule.triggerField.required");
    }

    [Fact]
    public void FormScopedTriggerNeedsNoField()
    {
        var draft = Valid();
        draft.Rules[0].Trigger = CollectAutomationContractValues.TriggerBeforeSubmit;
        draft.Rules[0].TriggerField = string.Empty;

        Assert.DoesNotContain(Evaluate(draft), e => e.FieldKey == CollectAutomationFieldKeys.RuleTriggerField(0));
    }

    [Fact]
    public void RuleWithNoActions_ErrorsOnRules()
    {
        var draft = Valid();
        draft.Rules[0].Actions.Clear();

        Assert.Contains(Evaluate(draft), e =>
            e.FieldKey == CollectAutomationFieldKeys.Rules && e.Code == "automation.rule.actions.empty");
    }

    [Fact]
    public void UnsupportedActionKind_ErrorsOnActionTarget()
    {
        var draft = Valid();
        draft.Rules[0].Actions[0].Kind = "teleport";

        Assert.Contains(Evaluate(draft), e =>
            e.FieldKey == CollectAutomationFieldKeys.ActionTarget(0, 0)
            && e.Code == "automation.action.kind.unsupported");
    }

    [Theory]
    [InlineData("set")]
    [InlineData("compute")]
    [InlineData("tag")]
    [InlineData("notify")]
    [InlineData("http")]
    [InlineData("open-url")]
    public void TargetedActionWithoutTarget_ErrorsOnActionTarget(string kind)
    {
        var draft = Valid();
        draft.Rules[0].Actions[0].Kind = kind;
        draft.Rules[0].Actions[0].Target = string.Empty;

        Assert.Contains(Evaluate(draft), e =>
            e.FieldKey == CollectAutomationFieldKeys.ActionTarget(0, 0)
            && e.Code == "automation.action.target.required");
    }

    [Theory]
    [InlineData("validate")]
    [InlineData("ai")]
    public void ExpressionOnlyActionNeedsNoTarget(string kind)
    {
        var draft = Valid();
        draft.Rules[0].Actions[0].Kind = kind;
        draft.Rules[0].Actions[0].Target = string.Empty;

        Assert.DoesNotContain(Evaluate(draft), e =>
            e.FieldKey == CollectAutomationFieldKeys.ActionTarget(0, 0)
            && e.Code == "automation.action.target.required");
    }

    [Fact]
    public void EveryFinding_IsBlocking_SoSaveIsGated()
    {
        // A drift between "Error/Blocker" severity and the editor's HasBlockingClientErrors save gate would
        // let a broken automation through; assert every finding the validator can emit blocks the save.
        var draft = Valid();
        draft.Name = string.Empty;
        draft.FormId = string.Empty;
        draft.MaxCascadeDepth = 99;
        draft.Rules[0].Trigger = "nope";
        draft.Rules[0].Actions[0].Kind = "nope";

        var errors = Evaluate(draft);
        Assert.NotEmpty(errors);
        Assert.All(errors, e => Assert.True(e.IsBlocking, $"{e.Code} is not blocking"));
    }
}
