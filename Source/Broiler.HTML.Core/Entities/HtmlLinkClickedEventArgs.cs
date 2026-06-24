using System;
using System.Collections.Generic;

namespace Broiler.HTML.Core.Entities;

public sealed class HtmlLinkClickedEventArgs(string link, Dictionary<string, string> attributes) : EventArgs
{
    public string Link { get; } = link;
    public Dictionary<string, string> Attributes { get; } = attributes;
    public bool Handled { get; set; }
    public override string ToString() => $"Link: {Link}, Handled: {Handled}";
}