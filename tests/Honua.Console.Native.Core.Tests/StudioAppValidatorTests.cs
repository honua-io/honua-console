using Honua.Console.Shell.Models;
using Honua.Console.Shell.Validation;

namespace Honua.Console.Native.Core.Tests;

/// <summary>
/// Docker-free unit coverage for <see cref="StudioAppValidator"/>, the Wave-4 client validator for the
/// Studio app builder. Each catalog rule (required page/route/binding/permission, route starts <c>/</c>,
/// unique routes, referential action.PageRoute, visibility + permission enum membership) is proven in its
/// pass and fail state, keyed by <see cref="StudioAppFieldKeys"/>.
/// </summary>
public sealed class StudioAppValidatorTests
{
    private static IReadOnlyList<ConsoleFieldError> Evaluate(StudioAppEditorState state) =>
        StudioAppValidator.Instance.Evaluate(state);

    private static StudioAppEditorState Valid()
    {
        var state = new StudioAppEditorState
        {
            Title = "Field operations",
            Visibility = "workspace",
        };
        state.Pages.Add(new StudioAppPageState { Route = "/map", Title = "Map", ComponentKind = "map", ContentBinding = "content:permits@v3" });
        state.Actions.Add(new StudioAppActionState { Name = "submit", PageRoute = "/map", RequiredPermission = "editor" });
        return state;
    }

    [Fact]
    public void ValidApp_ProducesNoErrors() => Assert.Empty(Evaluate(Valid()));

    [Fact]
    public void MissingTitle_BlocksOnTitle()
    {
        var state = Valid();
        state.Title = " ";

        var error = Assert.Single(Evaluate(state), e => e.FieldKey == StudioAppFieldKeys.Title);
        Assert.Equal(ConsoleValidationSeverity.Blocker, error.Severity);
    }

    [Fact]
    public void NoPages_BlocksOnPages()
    {
        var state = Valid();
        state.Pages.Clear();

        Assert.Contains(Evaluate(state), e =>
            e.FieldKey == StudioAppFieldKeys.Pages && e.Severity == ConsoleValidationSeverity.Blocker);
    }

    [Fact]
    public void RouteWithoutLeadingSlash_ErrorsWithFormatCode()
    {
        var state = Valid();
        state.Pages[0].Route = "map";

        var error = Assert.Single(Evaluate(state), e => e.FieldKey == StudioAppFieldKeys.PageRoute(0));
        Assert.Equal("app.page.route.format", error.Code);
    }

    [Fact]
    public void MissingContentBinding_BlocksOnPageBinding()
    {
        var state = Valid();
        state.Pages[0].ContentBinding = "";

        Assert.Contains(Evaluate(state), e =>
            e.FieldKey == StudioAppFieldKeys.PageBinding(0) && e.Severity == ConsoleValidationSeverity.Blocker);
    }

    [Fact]
    public void DuplicateRoutes_ErrorOnSecondPage()
    {
        var state = Valid();
        state.Pages.Add(new StudioAppPageState { Route = "/map", ContentBinding = "content:other@v1" });

        Assert.Contains(Evaluate(state), e =>
            e.FieldKey == StudioAppFieldKeys.PageRoute(1) && e.Code == "app.page.route.duplicate");
    }

    [Fact]
    public void ActionPageRoute_ReferencingUnknownPage_Errors()
    {
        var state = Valid();
        state.Actions[0].PageRoute = "/missing";

        var error = Assert.Single(Evaluate(state), e => e.FieldKey == StudioAppFieldKeys.ActionPageRoute(0));
        Assert.Equal("app.action.pageRoute.unresolved", error.Code);
    }

    [Fact]
    public void ActionWithoutPermission_BlocksOnPermission()
    {
        var state = Valid();
        state.Actions[0].RequiredPermission = "";

        Assert.Contains(Evaluate(state), e =>
            e.FieldKey == StudioAppFieldKeys.ActionPermission(0) && e.Severity == ConsoleValidationSeverity.Blocker);
    }

    [Fact]
    public void UnknownPermission_ErrorsWithInvalidCode()
    {
        var state = Valid();
        state.Actions[0].RequiredPermission = "superuser";

        var error = Assert.Single(Evaluate(state), e => e.FieldKey == StudioAppFieldKeys.ActionPermission(0));
        Assert.Equal("app.action.permission.invalid", error.Code);
    }

    [Fact]
    public void UnknownVisibility_ErrorsOnVisibility()
    {
        var state = Valid();
        state.Visibility = "galactic";

        var error = Assert.Single(Evaluate(state), e => e.FieldKey == StudioAppFieldKeys.Visibility);
        Assert.Equal("app.visibility.invalid", error.Code);
    }

    [Fact]
    public void ServerErrorBinder_ResolvesActionAndPagePointers()
    {
        var bound = StudioAppServerErrorBinder.Map(
        [
            new StudioAppValidationItem("error", "app.page.route.format", "/pages/0/route", "bad route"),
            new StudioAppValidationItem("error", "app.action.pageRoute.unresolved", "/actions/1/pageRoute", "bad ref"),
        ]);

        Assert.Contains(bound, e => e.FieldKey == StudioAppFieldKeys.PageRoute(0));
        Assert.Contains(bound, e => e.FieldKey == StudioAppFieldKeys.ActionPageRoute(1));
    }
}
