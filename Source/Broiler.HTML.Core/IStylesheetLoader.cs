using System.Collections.Generic;

namespace Broiler.HTML.Core;

/// <summary>
/// Interface for loading external stylesheets during DOM parsing.
/// Breaks the direct dependency between <c>DomParser</c> and the concrete
/// <c>StylesheetLoadHandler</c> class.
/// </summary>
internal interface IStylesheetLoader
{
    /// <summary>
    /// Loads a stylesheet from the specified source.
    /// </summary>
    /// <param name="src">The stylesheet source (URL or path).</param>
    /// <param name="attributes">The HTML attributes of the link element.</param>
    /// <param name="stylesheet">The loaded stylesheet text, or null.</param>
    /// <param name="styleSheet">The loaded shared stylesheet model, or null.</param>
    void LoadStylesheet(string src, Dictionary<string, string> attributes, out string stylesheet, out Broiler.CSS.CssStyleSheet styleSheet);
}
