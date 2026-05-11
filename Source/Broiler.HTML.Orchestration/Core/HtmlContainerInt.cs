using Broiler.HTML.Adapters.Adapters;
using Broiler.HTML.Core.Core;
using Broiler.HTML.Core.Core.Entities;
using Broiler.HTML.Core.Core.IR;
using Broiler.HTML.CSS.Core.Parse;
using Broiler.HTML.Dom.Core.Dom;
using Broiler.HTML.Dom.Core.Utils;
using Broiler.HTML.Orchestration.Core.Handlers;
using Broiler.HTML.Orchestration.Core.IR;
using Broiler.HTML.Orchestration.Core.Parse;
using Broiler.HTML.Primitives.Adapters.Entities;
using Broiler.HTML.Rendering.Core.Handlers;
using Broiler.HTML.Utils.Core.Utils;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;

namespace Broiler.HTML.Orchestration.Core;

public sealed class HtmlContainerInt : IHtmlContainerInt, IDisposable
{
    private List<HoverBoxBlock> _hoverBoxes;
    private HTML.Core.Core.ISelectionHandler _selectionHandler;
    private ImageDownloader _imageDownloader;
    private CssData _cssData;
    private bool _loadComplete;
    private int _marginTop;
    private int _marginBottom;
    private int _marginLeft;
    private int _marginRight;
    private readonly IHandlerFactory _handlerFactory;

    /// <summary>
    /// The most recent fragment tree snapshot, built after layout completes.
    /// </summary>
    internal Fragment LatestFragmentTree { get; private set; }

    /// <summary>
    /// The most recent display list produced by the paint path.
    /// Populated after each <see cref="PerformPaint"/> call.
    /// </summary>
    internal DisplayList LatestDisplayList { get; private set; }

    internal HtmlContainerInt(IAdapter adapter, IHandlerFactory handlerFactory)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        ArgumentNullException.ThrowIfNull(handlerFactory);

        Adapter = adapter;
        _handlerFactory = handlerFactory;
        CssParser = new CssParser(adapter);
    }

    internal IAdapter Adapter { get; }

    internal CssParser CssParser { get; }

    public event EventHandler LoadComplete;
    public event EventHandler<HtmlLinkClickedEventArgs> LinkClicked;
    public event EventHandler<HtmlRefreshEventArgs> Refresh;
    public event EventHandler<HtmlScrollEventArgs> ScrollChange;
    public event EventHandler<HtmlRenderErrorEventArgs> RenderError;
    public event EventHandler<HtmlStylesheetLoadEventArgs> StylesheetLoad;
    public event EventHandler<HtmlImageLoadEventArgs> ImageLoad;

    public CssData CssData => _cssData;

    public bool AvoidGeometryAntialias { get; set; }

    public bool AvoidAsyncImagesLoading { get; set; }

    public bool AvoidImagesLateLoading { get; set; }

    public bool IsSelectionEnabled { get; set; } = true;

    public bool IsContextMenuEnabled { get; set; } = true;

    public PointF ScrollOffset { get; set; }

    public PointF Location { get; set; }

    public SizeF MaxSize { get; set; }

    /// <summary>
    /// Optional base URL used to resolve relative <c>href</c> values in links.
    /// When set, relative paths (e.g. <c>./page.html</c>, <c>../section/index.html</c>)
    /// are resolved against this URL before navigation.
    /// </summary>
    public string BaseUrl { get; set; }

    public SizeF ActualSize { get; set; }

    public SizeF PageSize { get; set; }

    public SizeF ViewportSize
    {
        get
        {
            float w = MaxSize.Width > 0 ? Math.Min(MaxSize.Width, PageSize.Width) : PageSize.Width;
            float h = MaxSize.Height > 0 ? Math.Min(MaxSize.Height, PageSize.Height) : PageSize.Height;
            return new SizeF(w, h);
        }
    }

    public int MarginTop
    {
        get { return _marginTop; }
        set
        {
            if (value > -1)
                _marginTop = value;
        }
    }

    public int MarginBottom
    {
        get { return _marginBottom; }
        set
        {
            if (value > -1)
                _marginBottom = value;
        }
    }

    public int MarginLeft
    {
        get { return _marginLeft; }
        set
        {
            if (value > -1)
                _marginLeft = value;
        }
    }

    public int MarginRight
    {
        get { return _marginRight; }
        set
        {
            if (value > -1)
                _marginRight = value;
        }
    }

    public void SetMargins(int value)
    {
        if (value > -1)
            _marginBottom = _marginLeft = _marginTop = _marginRight = value;
    }

    public string SelectedText => _selectionHandler.GetSelectedText();
    public string SelectedHtml => _selectionHandler.GetSelectedHtml();
    internal CssBox Root { get; private set; }
    internal Color SelectionForeColor { get; set; }
    internal Color SelectionBackColor { get; set; }
    public void SetHtml(string htmlSource, CssData baseCssData = null, string baseUrl = null)
    {
        Clear();

        if (baseUrl != null)
            BaseUrl = baseUrl;

        if (string.IsNullOrEmpty(htmlSource))
            return;

        _loadComplete = false;
        _cssData = baseCssData ?? Adapter.DefaultCssData;

        var baseUri = new Uri(baseUrl ?? "/", UriKind.RelativeOrAbsolute);
        DomParser parser = new(CssParser, new StylesheetLoadHandler(this));
        Root = parser.GenerateCssTree(htmlSource, this, ref _cssData, baseUri);

        if (Root == null)
            return;

        // Load @font-face fonts before layout so custom families are available.
        LoadFontFacesFromCssData(baseUrl);

        _selectionHandler = _handlerFactory.CreateSelectionHandler(Root);
        _imageDownloader = new ImageDownloader();
    }

    /// <summary>
    /// Iterates parsed <c>@font-face</c> rules from <see cref="_cssData"/> and
    /// loads each font file via the platform adapter.  Relative <c>src</c> URLs
    /// are resolved against <paramref name="baseUrl"/>.
    /// </summary>
    private void LoadFontFacesFromCssData(string baseUrl)
    {
        if (_cssData?.FontFaces == null || _cssData.FontFaces.Count == 0)
            return;

        foreach (var face in _cssData.FontFaces)
        {
            if (string.IsNullOrEmpty(face.Src) || string.IsNullOrEmpty(face.Family))
                continue;

            var src = face.Src.Trim('\'', '"');

            // Skip remote URLs — only local file paths are supported.
            if (src.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                src.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                continue;

            string resolved = ResolveLocalFontPath(src, baseUrl);
            if (!string.IsNullOrEmpty(resolved) && File.Exists(resolved))
                Adapter.LoadFontFromFile(resolved, face.Family);
        }
    }

    /// <summary>
    /// Resolves a font <paramref name="src"/> path against the document
    /// <paramref name="baseUrl"/>.  Returns an absolute file system path,
    /// or <c>null</c> if resolution fails.
    /// </summary>
    private static string ResolveLocalFontPath(string src, string baseUrl)
    {
        // Already absolute file path
        if (Path.IsPathRooted(src) && File.Exists(src))
            return src;

        // Resolve against base URL (file-based)
        if (!string.IsNullOrEmpty(baseUrl))
        {
            // Try as file URI
            if (Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri) && baseUri.IsFile)
            {
                string dir = Path.GetDirectoryName(baseUri.LocalPath);
                if (!string.IsNullOrEmpty(dir))
                {
                    string combined = Path.GetFullPath(Path.Combine(dir, src));
                    if (File.Exists(combined))
                        return combined;
                }
            }

            // Try as plain file system path
            string baseDir = Path.GetDirectoryName(baseUrl);
            if (!string.IsNullOrEmpty(baseDir))
            {
                string combined = Path.GetFullPath(Path.Combine(baseDir, src));
                if (File.Exists(combined))
                    return combined;
            }
        }

        return null;
    }

    public void Clear()
    {
        if (Root == null)
            return;

        Root.Dispose();
        Root = null;

        _selectionHandler?.Dispose();
        _selectionHandler = null;

        _imageDownloader?.Dispose();
        _imageDownloader = null;

        _hoverBoxes = null;
    }

    public void ClearSelection()
    {
        if (_selectionHandler == null)
            return;

        _selectionHandler.ClearSelection();
        RequestRefresh(false);
    }

    public string GetHtml(HtmlGenerationStyle styleGen = HtmlGenerationStyle.Inline) => DomUtils.GenerateHtml(Root, styleGen);

    public string GetAttributeAt(PointF location, string attribute)
    {
        ArgumentNullException.ThrowIfNull(attribute);

        var cssBox = DomUtils.GetCssBox(Root, OffsetByScroll(location));
        return cssBox != null ? DomUtils.GetAttribute(cssBox, attribute) : null;
    }

    public List<LinkElementData<RectangleF>> GetLinks()
    {
        var linkBoxes = new List<CssBox>();
        DomUtils.GetAllLinkBoxes(Root, linkBoxes);

        var linkElements = new List<LinkElementData<RectangleF>>();

        foreach (var box in linkBoxes)
            linkElements.Add(new LinkElementData<RectangleF>(box.GetAttribute("id"), box.GetAttribute("href"), CommonUtils.GetFirstValueOrDefault(box.Rectangles, box.Bounds)));

        return linkElements;
    }

    public string GetLinkAt(PointF location)
    {
        var link = DomUtils.GetLinkBox(Root, OffsetByScroll(location));
        return link?.HrefLink;
    }

    public RectangleF? GetElementRectangle(string elementId)
    {
        ArgumentException.ThrowIfNullOrEmpty(elementId);

        var box = DomUtils.GetBoxById(Root, elementId.ToLower());
        return box != null ? CommonUtils.GetFirstValueOrDefault(box.Rectangles, box.Bounds) : null;
    }

    public void PerformLayout(RGraphics g)
    {
        ArgumentNullException.ThrowIfNull(g);

        ActualSize = SizeF.Empty;
        if (Root == null)
            return;

        // Set viewport dimensions for CSS viewport-relative units (vh, vw, vmin, vmax).
        // MaxSize represents the actual rendering viewport when set; PageSize is the
        // fallback (may be 99999 in auto-size scenarios).
        float vpW = MaxSize.Width > 0 ? Math.Min(MaxSize.Width, PageSize.Width) : PageSize.Width;
        float vpH = MaxSize.Height > 0 ? Math.Min(MaxSize.Height, PageSize.Height) : PageSize.Height;
        CssValueParser.SetViewportSize(vpW, vpH);

        // if width is not restricted we set it to large value to get the actual later
        // CSS2.1 §10.5: Percentage heights on the root element resolve against
        // the initial containing block, whose height is the viewport height.
        // Set the root box's height to the viewport height so that
        // html { height: 100% } resolves correctly.
        float rootH = MaxSize.Height > 0 ? vpH : 0;
        Root.Size = new SizeF(MaxSize.Width > 0 ? MaxSize.Width : 99999, rootH);
        Root.Location = Location;
        Root.PerformLayout(g);

        if (MaxSize.Width <= 0.1)
        {
            // in case the width is not restricted we need to double layout, first will find the width so second can layout by it (center alignment)
            Root.Size = new SizeF((int)Math.Ceiling(ActualSize.Width), 0);
            ActualSize = SizeF.Empty;
            Root.PerformLayout(g);
        }

        if (!_loadComplete)
        {
            _loadComplete = true;
            LoadComplete?.Invoke(this, EventArgs.Empty);
        }

        // Build fragment tree after layout — consumed by PaintWalker during paint.
        LatestFragmentTree = FragmentTreeBuilder.Build(Root);
    }

    public void PerformPaint(RGraphics g)
    {
        ArgumentNullException.ThrowIfNull(g);

        RectangleF viewport;
        if (MaxSize.Height > 0)
        {
            viewport = new RectangleF(Location.X, Location.Y, Math.Min(MaxSize.Width, PageSize.Width), Math.Min(MaxSize.Height, PageSize.Height));
        }
        else
        {
            viewport = new RectangleF(MarginLeft, MarginTop, PageSize.Width, PageSize.Height);
        }

        g.PushClip(viewport);

        if (LatestFragmentTree != null)
        {
            // When scrolling, compute the viewport in layout-space coordinates so that
            // PaintWalker generates a canvas background that covers the visible area
            // after the scroll offset is applied.
            var paintViewport = viewport;
            bool hasScroll = ScrollOffset.X != 0 || ScrollOffset.Y != 0;
            if (hasScroll)
            {
                paintViewport = new RectangleF(
                    viewport.X - ScrollOffset.X,
                    viewport.Y - ScrollOffset.Y,
                    viewport.Width,
                    viewport.Height);
            }

            // Paint path: Fragment tree → DisplayList → RGraphics
            // Pass viewport for CSS2.1 §14.2 canvas background propagation.
            var displayList = PaintWalker.Paint(LatestFragmentTree, paintViewport);

            // Apply scroll offset: shift all display items so that content scrolls
            // within the fixed viewport clip.
            if (hasScroll)
            {
                var offsetItems = new List<DisplayItem>(displayList.Items);
                PaintWalker.OffsetDisplayItems(offsetItems, 0, ScrollOffset.X, ScrollOffset.Y);
                displayList = new DisplayList { Items = offsetItems };
            }

            LatestDisplayList = displayList;
            RGraphicsRasterBackend.Instance.Render(displayList, g);
        }

        g.PopClip();
    }

    public void HandleMouseDown(object parent, PointF location)
    {
        ArgumentNullException.ThrowIfNull(parent);

        try
        {
            _selectionHandler?.HandleMouseDown(parent, OffsetByScroll(location), IsMouseInContainer(location));
        }
        catch (Exception ex)
        {
            ReportError(HtmlRenderErrorType.KeyboardMouse, "Failed mouse down handle", ex);
        }
    }

    public void HandleMouseUp(object parent, PointF location, RMouseEvent e)
    {
        ArgumentNullException.ThrowIfNull(parent);

        try
        {
            if (_selectionHandler == null || !IsMouseInContainer(location))
                return;

            var ignore = _selectionHandler.HandleMouseUp(parent, e.LeftButton);
            if (!ignore && e.LeftButton)
            {
                var loc = OffsetByScroll(location);
                var link = DomUtils.GetLinkBox(Root, loc);
                if (link != null)
                    HandleLinkClicked(parent, location, link);
            }
        }
        catch (HtmlLinkClickedException)
        {
            throw;
        }
        catch (Exception ex)
        {
            ReportError(HtmlRenderErrorType.KeyboardMouse, "Failed mouse up handle", ex);
        }
    }

    public void HandleMouseDoubleClick(object parent, PointF location)
    {
        ArgumentNullException.ThrowIfNull(parent);

        try
        {
            if (_selectionHandler != null && IsMouseInContainer(location))
                _selectionHandler.SelectWord(parent, OffsetByScroll(location));
        }
        catch (Exception ex)
        {
            ReportError(HtmlRenderErrorType.KeyboardMouse, "Failed mouse double click handle", ex);
        }
    }

    public void HandleMouseMove(object parent, PointF location)
    {
        ArgumentNullException.ThrowIfNull(parent);

        try
        {
            var loc = OffsetByScroll(location);
            if (_selectionHandler != null && IsMouseInContainer(location))
                _selectionHandler.HandleMouseMove(parent, loc);
        }
        catch (Exception ex)
        {
            ReportError(HtmlRenderErrorType.KeyboardMouse, "Failed mouse move handle", ex);
        }
    }

    public void HandleMouseLeave(object parent)
    {
        ArgumentNullException.ThrowIfNull(parent);

        try
        {
            _selectionHandler?.HandleMouseLeave(parent);
        }
        catch (Exception ex)
        {
            ReportError(HtmlRenderErrorType.KeyboardMouse, "Failed mouse leave handle", ex);
        }
    }

    public void HandleKeyDown(object parent, RKeyEvent e)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(e);

        try
        {
            if (!e.Control || _selectionHandler == null)
                return;

            // select all
            if (e.AKeyCode)
                _selectionHandler.SelectAll(parent);

            // copy currently selected text
            if (e.CKeyCode)
                _selectionHandler.CopySelectedHtml();
        }
        catch (Exception ex)
        {
            ReportError(HtmlRenderErrorType.KeyboardMouse, "Failed key down handle", ex);
        }
    }

    internal void RaiseHtmlStylesheetLoadEvent(HtmlStylesheetLoadEventArgs args)
    {
        try
        {
            StylesheetLoad?.Invoke(this, args);
        }
        catch (Exception ex)
        {
            ReportError(HtmlRenderErrorType.CssParsing, "Failed stylesheet load event", ex);
        }
    }

    internal void RaiseHtmlImageLoadEvent(HtmlImageLoadEventArgs args)
    {
        try
        {
            ImageLoad?.Invoke(this, args);
        }
        catch (Exception ex)
        {
            ReportError(HtmlRenderErrorType.Image, "Failed image load event", ex);
        }
    }

    public void RequestRefresh(bool layout)
    {
        try
        {
            Refresh?.Invoke(this, new HtmlRefreshEventArgs(layout));
        }
        catch (Exception ex)
        {
            ReportError(HtmlRenderErrorType.General, "Failed refresh request", ex);
        }
    }

    internal void ReportError(HtmlRenderErrorType type, string message, Exception exception = null)
    {
        try
        {
            RenderError?.Invoke(this, new HtmlRenderErrorEventArgs(type, message, exception));
        }
        catch
        { }
    }

    internal void HandleLinkClicked(object parent, PointF location, CssBox link)
    {
        // Resolve the target URL: for <a> links use href, for form submit
        // buttons walk up to the enclosing <form> and use its action attribute.
        string targetUrl = link.HrefLink;
        if (string.IsNullOrEmpty(targetUrl) && IsFormSubmitControl(link))
        {
            targetUrl = FindFormAction(link);
        }

        EventHandler<HtmlLinkClickedEventArgs> clickHandler = LinkClicked;
        if (clickHandler != null)
        {
            var args = new HtmlLinkClickedEventArgs(ResolveHref(targetUrl ?? string.Empty), link.HtmlTag.Attributes);
            try
            {
                clickHandler(this, args);
            }
            catch (Exception ex)
            {
                throw new HtmlLinkClickedException("Error in link clicked intercept", ex);
            }
            if (args.Handled)
                return;
        }

        if (string.IsNullOrEmpty(targetUrl))
            return;

        if (targetUrl == "#")
        {
            EventHandler<HtmlScrollEventArgs> scrollHandler = ScrollChange;
            if (scrollHandler != null)
            {
                scrollHandler(this, new HtmlScrollEventArgs(PointF.Empty));
                HandleMouseMove(parent, location);
            }
        }
        else if (targetUrl.StartsWith("#") && targetUrl.Length > 1)
        {
            EventHandler<HtmlScrollEventArgs> scrollHandler = ScrollChange;
            if (scrollHandler != null)
            {
                var rect = GetElementRectangle(targetUrl.Substring(1));
                if (rect.HasValue)
                {
                    scrollHandler(this, new HtmlScrollEventArgs(rect.Value.Location));
                    HandleMouseMove(parent, location);
                }
            }
        }
        else
        {
            var href = ResolveHref(targetUrl);
            var nfo = new ProcessStartInfo(href) { UseShellExecute = true };
            Process.Start(nfo);

        }
    }

    /// <summary>
    /// Returns <c>true</c> if the given box represents a form submit control
    /// (<c>&lt;input type="submit"&gt;</c>, <c>&lt;button&gt;</c>, etc.).
    /// </summary>
    private static bool IsFormSubmitControl(CssBox box)
    {
        if (box.HtmlTag == null) return false;
        var name = box.HtmlTag.Name;
        if (name.Equals("button", StringComparison.OrdinalIgnoreCase))
            return true;
        if (name.Equals("input", StringComparison.OrdinalIgnoreCase))
        {
            var inputType = box.HtmlTag.TryGetAttribute("type")?.ToLowerInvariant() ?? "text";
            return inputType is "submit" or "button" or "reset";
        }
        return false;
    }

    /// <summary>
    /// Walks up the box tree from a form submit control to find the
    /// enclosing <c>&lt;form&gt;</c> element and returns its <c>action</c>
    /// attribute value.  Returns <c>null</c> if no form is found.
    /// </summary>
    private static string FindFormAction(CssBox box)
    {
        var current = box.ParentBox;
        while (current != null)
        {
            if (current.HtmlTag != null &&
                current.HtmlTag.Name.Equals("form", StringComparison.OrdinalIgnoreCase))
            {
                return current.HtmlTag.TryGetAttribute("action");
            }
            current = current.ParentBox;
        }
        return null;
    }

    internal void AddHoverBox(CssBox box, CssBlock block)
    {
        ArgumentNullException.ThrowIfNull(box);
        ArgumentNullException.ThrowIfNull(block);

        _hoverBoxes ??= [];
        _hoverBoxes.Add(new HoverBoxBlock(box, block));
    }

    /// <summary>
    /// Resolves an href value against <see cref="BaseUrl"/> when the href is a
    /// relative path. If <see cref="BaseUrl"/> is not set or the href is already
    /// absolute, the original href is returned unchanged.
    /// </summary>
    internal string ResolveHref(string href)
    {
        if (string.IsNullOrEmpty(BaseUrl))
            return href;

        if (Uri.TryCreate(href, UriKind.Absolute, out _))
            return href;

        if (Uri.TryCreate(BaseUrl, UriKind.Absolute, out var baseUri))
        {
            var resolved = new Uri(baseUri, href);
            return resolved.AbsoluteUri;
        }

        return href;
    }

    #region IHtmlContainerInt

    void IHtmlContainerInt.ReportError(HtmlRenderErrorType type, string message, Exception exception)
        => ReportError(type, message, exception);

    Color IHtmlContainerInt.SelectionForeColor => SelectionForeColor;

    Color IHtmlContainerInt.SelectionBackColor => SelectionBackColor;

    void IHtmlContainerInt.RaiseHtmlImageLoadEvent(HtmlImageLoadEventArgs args)
        => RaiseHtmlImageLoadEvent(args);

    PointF IHtmlContainerInt.RootLocation => Root?.Location ?? PointF.Empty;

    RFont IHtmlContainerInt.GetFont(string family, double size, FontStyle style) => Adapter.GetFont(family, size, style);

    Color IHtmlContainerInt.ParseColor(string colorStr) => CssParser.ParseColor(colorStr);

    RImage IHtmlContainerInt.ConvertImage(object image) => Adapter.ConvertImage(image);

    RImage IHtmlContainerInt.ImageFromStream(Stream stream) => Adapter.ImageFromStream(stream);

    RImage IHtmlContainerInt.GetLoadingImage() => Adapter.GetLoadingImage();

    RImage IHtmlContainerInt.GetLoadingFailedImage() => Adapter.GetLoadingFailedImage();

    void IHtmlContainerInt.DownloadImage(Uri uri, string filePath, bool async, Action<Uri, string, Exception, bool> callback)
        => _imageDownloader?.DownloadImage(uri, filePath, async, (imageUri, fp, error, canceled) => callback(imageUri, fp, error, canceled));

    IImageLoadHandler IHtmlContainerInt.CreateImageLoadHandler(ActionInt<RImage, RectangleF, bool> loadCompleteCallback)
        => new ImageLoadHandler(this, loadCompleteCallback);

    void IHtmlContainerInt.AddHoverBox(object box, CssBlock block)
        => AddHoverBox((CssBox)box, block);

    CssData IHtmlContainerInt.CssData => _cssData;

    CssData IHtmlContainerInt.DefaultCssData => Adapter.DefaultCssData;

    CssBlock IHtmlContainerInt.ParseCssBlock(string className, string blockSource)
        => CssParser.ParseCssBlock(className, blockSource);

    #endregion

    public void Dispose() => Dispose(true);


    private PointF OffsetByScroll(PointF location) => new(location.X - ScrollOffset.X, location.Y - ScrollOffset.Y);

    private bool IsMouseInContainer(PointF location)
    {
        return location.X >= Location.X
            && location.X <= Location.X + ActualSize.Width
            && location.Y >= Location.Y + ScrollOffset.Y
            && location.Y <= Location.Y + ScrollOffset.Y + ActualSize.Height;
    }

    private void Dispose(bool all)
    {
        try
        {
            if (all)
            {
                LinkClicked = null;
                Refresh = null;
                RenderError = null;
                StylesheetLoad = null;
                ImageLoad = null;
            }

            _cssData = null;

            Root?.Dispose();
            Root = null;

            _selectionHandler?.Dispose();
            _selectionHandler = null;
        }
        catch
        { }
    }
}
