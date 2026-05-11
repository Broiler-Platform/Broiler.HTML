using System;
using System.Drawing;

namespace Broiler.HTML.Core.Core;

/// <summary>
/// Interface for selection handling at the container/orchestration level.
/// Breaks the dependency between <c>HtmlContainerInt</c> (in Orchestration)
/// and the concrete <c>SelectionHandler</c> class (in the façade).
/// </summary>
internal interface ISelectionHandler : IDisposable
{
    void HandleMouseDown(object parent, PointF loc, bool isMouseInContainer);
    bool HandleMouseUp(object parent, bool leftMouseButton);
    void HandleMouseMove(object parent, PointF loc);
    void HandleMouseLeave(object parent);
    void SelectWord(object parent, PointF loc);
    void SelectAll(object parent);
    void CopySelectedHtml();
    string GetSelectedText();
    string GetSelectedHtml();
    void ClearSelection();
}
