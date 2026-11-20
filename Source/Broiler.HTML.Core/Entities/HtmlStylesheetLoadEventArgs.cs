using System;
using System.Collections.Generic;
using Broiler.CSS;

namespace Broiler.HTML.Core.Entities;

public sealed class HtmlStylesheetLoadEventArgs : EventArgs
{
    internal HtmlStylesheetLoadEventArgs(string src, Dictionary<string, string> attributes)
    {
        Src = src;
        Attributes = attributes;
    }

    public string Src { get; }
    public Dictionary<string, string> Attributes { get; }
    public string? SetSrc { get; set; }
    public string? SetStyleSheet { get; set; }

    public CssStyleSheet? SetStyleSheetModel { get; set; }
}
