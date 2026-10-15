using System.Reflection;
using Broiler.Dom;
using Broiler.HTML.Dom.Parse;

namespace Broiler.HTML.Tests;

/// <summary>
/// Guards the DOM/HTML transition's retirements: the renderer's own copies of DOM semantics that were
/// replaced by the shared <c>Broiler.Dom.Html</c> queries stay gone, and the layout-tree coordinator keeps
/// its name.
/// </summary>
/// <remarks>
/// <para>
/// Each guard names the former implementation point, so reintroducing the copy there fails here; the
/// behavior itself is held by the focused suites (<see cref="VoidElementClassificationTests"/>,
/// <see cref="ProgressMeterFillTests"/>, <see cref="DocumentBaseUrlTests"/>,
/// <see cref="TableCellHintTests"/>, <see cref="FormControlFallbackTests"/>). Renderer-specific tag
/// classifications remain legitimate, so nothing here bans tag sets in general.
/// </para>
/// <para>
/// Historical mentions of the old names in documentation are not violations, and none of these reads
/// a private field name: the guards look for types and methods by the role they had.
/// </para>
/// </remarks>
public sealed class ArchitectureBoundaryTests
{
    private static readonly Assembly Core = typeof(Broiler.HTML.Core.Entities.HtmlGenerationStyle).Assembly;
    private static readonly Assembly HtmlDom = typeof(HtmlParserDump).Assembly;
    private static readonly Assembly Orchestration = typeof(Broiler.HTML.Orchestration.HtmlContainerInt).Assembly;

    private const BindingFlags AnyStatic = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    [Fact]
    public void The_Renderers_Own_Void_List_Is_Retired()
    {
        Assert.Null(Core.GetType("Broiler.HTML.Core.Utils.HtmlUtils"));
        Assert.DoesNotContain(
            AllTypes(Core, HtmlDom, Orchestration).SelectMany(static t => t.GetMethods(AnyStatic | BindingFlags.DeclaredOnly)),
            static m => m.Name == "IsSingleTag");
    }

    [Fact]
    public void The_Base_Url_Is_Not_Selected_From_The_Box_Tree()
    {
        var methods = typeof(DocumentBaseUrl).GetMethods(AnyStatic | BindingFlags.DeclaredOnly);

        Assert.DoesNotContain(methods, static m => m.Name is "Apply" or "Rebase");
        Assert.All(
            methods.Where(static m => m.Name == "Resolve"),
            static m => Assert.Equal(typeof(DomNode), m.GetParameters()[0].ParameterType));
    }

    [Fact]
    public void The_Layout_Tree_Builder_Reads_No_Numeric_Attribute_Itself()
    {
        var builder = LayoutTreeBuilder();

        Assert.DoesNotContain(
            builder.GetMethods(AnyStatic | BindingFlags.Instance | BindingFlags.DeclaredOnly),
            static m => m.Name == "ReadNumericAttribute");
    }

    [Fact]
    public void The_Coordinator_Is_The_Layout_Tree_Builder()
    {
        Assert.Null(Orchestration.GetType("Broiler.HTML.Orchestration.Parse.DomParser"));
        Assert.DoesNotContain(AllTypes(Orchestration), static t => t.Name == "DomParser");

        var builder = LayoutTreeBuilder();
        Assert.Equal(2, builder.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly).Count(static m => m.Name == "BuildLayoutTree"));
        Assert.DoesNotContain(builder.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly), static m => m.Name == "GenerateCssTree");
    }

    private static Type LayoutTreeBuilder() =>
        Orchestration.GetType("Broiler.HTML.Orchestration.Parse.LayoutTreeBuilder")
        ?? throw new InvalidOperationException("LayoutTreeBuilder is missing.");

    private static IEnumerable<Type> AllTypes(params Assembly[] assemblies) =>
        assemblies.SelectMany(static a => a.GetTypes());
}
