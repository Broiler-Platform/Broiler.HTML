namespace Broiler.HTML.Core.Core;

/// <summary>
/// Factory interface for creating handler instances in the orchestration layer.
/// Breaks the dependency between <c>HtmlContainerInt</c> (in Orchestration)
/// and concrete handler constructors (in the façade).
/// </summary>
internal interface IHandlerFactory
{
    /// <summary>
    /// Creates a new selection handler for the given root box.
    /// </summary>
    /// <param name="root">The root <c>CssBox</c> (passed as object to avoid L4a dependency).</param>
    ISelectionHandler CreateSelectionHandler(object root);
}
