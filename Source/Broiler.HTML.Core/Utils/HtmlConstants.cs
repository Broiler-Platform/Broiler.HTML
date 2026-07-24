namespace Broiler.HTML.Utils;

internal static class HtmlConstants
{
    // The five tag/attribute names shared with layout are single-sourced from the
    // lower Broiler.Layout layer. The renderer already
    // references Broiler.Layout, and Broiler.Layout.HtmlConstants is public, so these
    // forward instead of re-declaring the literals: A / Hr / Iframe / Img / Href.
    public const string A = Layout.HtmlConstants.A;
    public const string Br = "br";
    public const string Caption = "caption";
    public const string Col = "col";
    public const string Colgroup = "colgroup";
    public const string Display = "display";
    public const string Font = "font";
    public const string Hr = Layout.HtmlConstants.Hr;
    public const string Iframe = Layout.HtmlConstants.Iframe;
    public const string Img = Layout.HtmlConstants.Img;
    public const string Input = "input";
    public const string Li = "li";
    public const string Ol = "ol";
    public const string Style = "style";
    public const string Table = "table";
    public const string Tbody = "tbody";
    public const string Td = "td";
    public const string Tfoot = "tfoot";
    public const string Th = "th";
    public const string Thead = "thead";
    public const string Tr = "tr";
    public const string Ul = "ul";

    public const string Align = "align";
    public const string Background = "background";
    public const string Bgcolor = "bgcolor";
    public const string Border = "border";
    public const string Bordercolor = "bordercolor";
    public const string Cellpadding = "cellpadding";
    public const string Cellspacing = "cellspacing";
    public const string Class = "class";
    public const string Color = "color";
    public const string content = "content";
    public const string Dir = "dir";
    public const string Face = "face";
    public const string Height = "height";
    public const string Href = Layout.HtmlConstants.Href;
    public const string Hspace = "hspace";
    public const string Nowrap = "nowrap";
    public const string Size = "size";
    public const string Valign = "valign";
    public const string Vspace = "vspace";
    public const string Width = "width";

    public const string Left = "left";
    public const string Right = "right";
    public const string Center = "center";
    public const string Justify = "justify";
}
