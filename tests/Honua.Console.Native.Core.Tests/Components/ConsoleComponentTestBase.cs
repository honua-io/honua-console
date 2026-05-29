using Bunit;

namespace Honua.Console.Native.Core.Tests.Components;

/// <summary>
/// Base class for bUnit component tests over the shared Razor library
/// (<c>Honua.Console.Shell</c>).
///
/// bUnit is the Definition of Done for Console components per the Console
/// Patterns Charter (section 1: "Unit/component tests via xUnit + bUnit").
/// These tests are fully server-independent: components render off a
/// <see cref="BunitContext"/> renderer with parameters supplied directly, so
/// no live honua-server, HTTP client, or SDK projection is required.
///
/// Conventions for future component tests:
/// <list type="bullet">
///   <item>Derive from this class to inherit a disposed-per-test <c>BunitContext</c>.</item>
///   <item>Render with <c>Render&lt;TComponent&gt;(p =&gt; p.Add(...))</c>.</item>
///   <item>Assert on rendered markup/structure, not on private members.</item>
///   <item>Keep tests free of any server-owned data binding (charter section 11).</item>
/// </list>
/// </summary>
public abstract class ConsoleComponentTestBase : BunitContext
{
}
