using System;
using System.Collections.Generic;

namespace Broiler.HTML.Core.Core.Entities;

public sealed class HtmlStylesheetLoadEventArgs : EventArgs
{
    internal HtmlStylesheetLoadEventArgs(string src, Dictionary<string, string> attributes)
    {
        Src = src;
        Attributes = attributes;
    }

    public string Src { get; }
    public Dictionary<string, string> Attributes { get; }
    public string SetSrc { get; set; }
    public string SetStyleSheet { get; set; }
    public CssData SetStyleSheetData { get; set; }
}