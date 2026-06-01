using Honua.Console.Shell.Models;
using Honua.Console.Shell.Validation;

namespace Honua.Console.Native.Core.Tests;

/// <summary>
/// Docker-free unit coverage for <see cref="StudioAnalysisValidator"/>, the Wave-4 client validator for the
/// Studio analysis builder. Each catalog rule (required method + inputs, LayerId &gt;= 0, parameter name
/// presence, unique output field names) is proven in its pass and fail state, keyed by
/// <see cref="StudioAnalysisFieldKeys"/>.
/// </summary>
public sealed class StudioAnalysisValidatorTests
{
    private static IReadOnlyList<ConsoleFieldError> Evaluate(StudioAnalysisPlanEditor state) =>
        StudioAnalysisValidator.Instance.Evaluate(state);

    private static StudioAnalysisPlanEditor Valid()
    {
        var plan = new StudioAnalysisPlanEditor { Title = "Buffer", Method = "buffer" };
        plan.Inputs.Add(new StudioAnalysisInputEditor { Role = "primary", ServiceId = "hydrants", LayerId = 0 });
        plan.Parameters.Add(new StudioAnalysisParameterEditor { Name = "distanceMeters", Value = "250" });
        plan.OutputSchema.Add(new StudioAnalysisOutputFieldEditor { Name = "buffer_distance", Type = "double" });
        return plan;
    }

    [Fact]
    public void ValidPlan_ProducesNoErrors() => Assert.Empty(Evaluate(Valid()));

    [Fact]
    public void MissingMethod_BlocksOnMethod()
    {
        var plan = Valid();
        plan.Method = "";

        var error = Assert.Single(Evaluate(plan), e => e.FieldKey == StudioAnalysisFieldKeys.Method);
        Assert.Equal(ConsoleValidationSeverity.Blocker, error.Severity);
    }

    [Fact]
    public void UnknownMethod_ErrorsWithInvalidCode()
    {
        var plan = Valid();
        plan.Method = "teleport";

        var error = Assert.Single(Evaluate(plan), e => e.FieldKey == StudioAnalysisFieldKeys.Method);
        Assert.Equal("analysis.method.invalid", error.Code);
    }

    [Fact]
    public void NoInputs_BlocksOnInputs()
    {
        var plan = Valid();
        plan.Inputs.Clear();

        Assert.Contains(Evaluate(plan), e =>
            e.FieldKey == StudioAnalysisFieldKeys.Inputs && e.Severity == ConsoleValidationSeverity.Blocker);
    }

    [Fact]
    public void NegativeLayerId_ErrorsOnThatInput()
    {
        var plan = Valid();
        plan.Inputs[0].LayerId = -1;

        var error = Assert.Single(Evaluate(plan), e => e.FieldKey == StudioAnalysisFieldKeys.InputLayerId(0));
        Assert.Equal("analysis.input.layerId.range", error.Code);
    }

    [Fact]
    public void ParameterWithoutName_ErrorsOnThatParameter()
    {
        var plan = Valid();
        plan.Parameters[0].Name = " ";

        Assert.Contains(Evaluate(plan), e => e.FieldKey == StudioAnalysisFieldKeys.ParameterName(0));
    }

    [Fact]
    public void DuplicateOutputFieldNames_ErrorOnSecond()
    {
        var plan = Valid();
        plan.OutputSchema.Add(new StudioAnalysisOutputFieldEditor { Name = "buffer_distance", Type = "double" });

        Assert.Contains(Evaluate(plan), e =>
            e.FieldKey == StudioAnalysisFieldKeys.OutputFieldName(1) && e.Code == "analysis.outputField.name.duplicate");
    }

    [Fact]
    public void EmptyOutputFieldName_BlocksOnThatField()
    {
        var plan = Valid();
        plan.OutputSchema[0].Name = "";

        Assert.Contains(Evaluate(plan), e =>
            e.FieldKey == StudioAnalysisFieldKeys.OutputFieldName(0) && e.Severity == ConsoleValidationSeverity.Blocker);
    }

    [Fact]
    public void ServerErrorBinder_ResolvesInputAndOutputPaths()
    {
        var bound = StudioAnalysisServerErrorBinder.Map(
        [
            new ConsoleFieldValidationError { Code = "analysis.content.savedQuery.layerId.range", Path = "/inputs/0/layerId", Message = "bad" },
            new ConsoleFieldValidationError { Code = "analysis.content.outputField", Path = "/outputSchema/2/name", Message = "dup" },
            new ConsoleFieldValidationError { Code = "analysis.content.name.required", Path = "/method", Message = "method" },
        ]);

        Assert.Contains(bound, e => e.FieldKey == StudioAnalysisFieldKeys.InputLayerId(0));
        Assert.Contains(bound, e => e.FieldKey == StudioAnalysisFieldKeys.OutputFieldName(2));
        Assert.Contains(bound, e => e.FieldKey == StudioAnalysisFieldKeys.Method);
    }
}
