using Broiler.HTML.Adapters;
using Broiler.HTML.Core;
using Broiler.HTML.Core.Entities;
using Broiler.HTML.Core.IR;
using Broiler.CSS;
using Broiler.HTML.Dom;
using Broiler.Layout;
using HtmlConstants = Broiler.HTML.Utils.HtmlConstants;
using CommonUtils = Broiler.HTML.Utils.CommonUtils;
using Broiler.HTML.Dom.Utils;
using Broiler.HTML.Orchestration.Core.IR;
using Broiler.HTML.Orchestration.Handlers;
using Broiler.HTML.Orchestration.Parse;
using Broiler.HTML.Primitives.Adapters.Entities;
using Broiler.HTML.Rendering.Handlers;
using Broiler.HTML.Utils;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net.Http;

using HtmlTag = Broiler.Layout.HtmlTag;
namespace Broiler.HTML.Orchestration;

public sealed class HtmlContainerInt : IHtmlContainerInt, IDisposable
{
    private HTML.Core.Core.ISelectionHandler _selectionHandler;
    private ImageDownloader _imageDownloader;
    private HtmlStyleSet _styleSet;
    private bool _loadComplete;
    private int _marginTop;
    private int _marginBottom;
    private int _marginLeft;
    private int _marginRight;
    private readonly IHandlerFactory _handlerFactory;
    private Broiler.Dom.DomDocument _boundDocument;
    private ulong _boundDocumentVersion;
    private HtmlStyleSet _boundBaseStyleSet;

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
    }

    internal IAdapter Adapter { get; }

    public event EventHandler LoadComplete;
    public event EventHandler<HtmlLinkClickedEventArgs> LinkClicked;
    public event EventHandler<HtmlRefreshEventArgs> Refresh;
    public event EventHandler<HtmlScrollEventArgs> ScrollChange;
    public event EventHandler<HtmlRenderErrorEventArgs> RenderError;
    public event EventHandler<HtmlStylesheetLoadEventArgs> StylesheetLoad;
    public event EventHandler<HtmlImageLoadEventArgs> ImageLoad;

    public HtmlStyleSet StyleSet => _styleSet;

    [Obsolete("Use StyleSet.")]
    public CssData CssData => new(_styleSet);

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

    /// <summary>
    /// Returns the canvas background propagated from the root or body box.
    /// Keeping this traversal beside the owned box tree prevents facade
    /// assemblies from depending on layout internals.
    /// </summary>
    public Color GetRootBackgroundColor()
    {
        if (Root == null)
            return Color.Empty;

        // Root is an anonymous wrapper; inspect it before the html/body boxes.
        var background = Root.ActualBackgroundColor;
        if (!background.IsEmpty && background.A > 0)
            return background;

        CssBox? htmlBox = null;
        foreach (var child in Root.Boxes)
        {
            if (!string.Equals(child.HtmlTag?.Name, "html", StringComparison.OrdinalIgnoreCase))
                continue;

            htmlBox = child;
            if (SuppressesCanvasBackgroundPropagation(child))
                return Color.Empty;

            background = child.ActualBackgroundColor;
            if (!background.IsEmpty && background.A > 0)
                return background;
            break;
        }

        if (htmlBox == null)
            return Color.Empty;

        foreach (var child in htmlBox.Boxes)
        {
            if (!string.Equals(child.HtmlTag?.Name, "body", StringComparison.OrdinalIgnoreCase))
                continue;

            if (SuppressesCanvasBackgroundPropagation(child))
                return Color.Empty;

            background = child.ActualBackgroundColor;
            if (!background.IsEmpty && background.A > 0)
                return background;
            break;
        }

        // An inline body may be nested under anonymous block wrappers.
        if (background.IsEmpty || background.A == 0)
        {
            var bodyBox = FindBodyBox(htmlBox);
            if (bodyBox != null)
            {
                if (SuppressesCanvasBackgroundPropagation(bodyBox))
                    return Color.Empty;

                background = bodyBox.ActualBackgroundColor;
                if (!background.IsEmpty && background.A > 0)
                    return background;
            }
        }

        return Color.Empty;
    }

    private static CssBox? FindBodyBox(CssBox parent, int depth = 0)
    {
        if (depth > 3)
            return null;

        foreach (var child in parent.Boxes)
        {
            if (string.Equals(child.HtmlTag?.Name, "body", StringComparison.OrdinalIgnoreCase))
                return child;

            if (child.HtmlTag == null && FindBodyBox(child, depth + 1) is { } nestedBody)
                return nestedBody;
        }

        return null;
    }

    private static bool SuppressesCanvasBackgroundPropagation(CssBox box)
    {
        if (string.Equals(box.Display, "none", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(box.Display, "contents", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var contain = box.Contain;
        return !string.IsNullOrEmpty(contain) &&
               (contain.Contains("paint", StringComparison.OrdinalIgnoreCase) ||
                contain.Contains("strict", StringComparison.OrdinalIgnoreCase) ||
                contain.Contains("content", StringComparison.OrdinalIgnoreCase));
    }
    internal Color SelectionForeColor { get; set; }
    internal Color SelectionBackColor { get; set; }

    internal Color ParseCssColor(string value)
    {
        if (Broiler.CSS.CssValueParser.TryParseColor(value, out var color))
            return Color.FromArgb(color.Alpha, color.Red, color.Green, color.Blue);

        return Adapter.GetColor(value);
    }

    [Obsolete("Use SetHtmlWithStyleSet.")]
    public void SetHtml(string htmlSource, CssData baseCssData = null, string baseUrl = null)
    {
#pragma warning disable CS0618
        SetHtmlWithStyleSet(htmlSource, baseCssData?.StyleSet, baseUrl);
#pragma warning restore CS0618
    }

    public void SetHtmlWithStyleSet(string htmlSource, HtmlStyleSet baseStyleSet = null, string baseUrl = null)
    {
        Clear();
        _boundDocument = null;

        if (baseUrl != null)
            BaseUrl = baseUrl;

        if (string.IsNullOrEmpty(htmlSource))
            return;

        var baseUri = new Uri(baseUrl ?? "/", UriKind.RelativeOrAbsolute);
        DomParser parser = new(new StylesheetLoadHandler(this));
        InitialiseRoot(
            baseStyleSet,
            baseUrl,
            (ref HtmlStyleSet styleSet) => parser.GenerateCssTree(htmlSource, this, ref styleSet, baseUri));
    }

    [Obsolete("Use SetDocumentWithStyleSet.")]
    public void SetDocument(Broiler.Dom.DomDocument document, CssData baseCssData = null, string baseUrl = null)
    {
#pragma warning disable CS0618
        SetDocumentWithStyleSet(document, baseCssData?.StyleSet, baseUrl);
#pragma warning restore CS0618
    }

    public void SetDocumentWithStyleSet(Broiler.Dom.DomDocument document, HtmlStyleSet baseStyleSet = null, string baseUrl = null)
    {
        ArgumentNullException.ThrowIfNull(document);

        Clear();
        _boundDocument = document;
        _boundDocumentVersion = document.Version;
        _boundBaseStyleSet = baseStyleSet;

        if (baseUrl != null)
            BaseUrl = baseUrl;

        BuildBoundDocument();
    }

    private delegate CssBox CssTreeFactory(ref HtmlStyleSet styleSet);

    private void InitialiseRoot(
        HtmlStyleSet baseStyleSet,
        string baseUrl,
        CssTreeFactory createTree)
    {
        _loadComplete = false;
        _styleSet = baseStyleSet ?? Adapter.DefaultStyleSet;
        Root = createTree(ref _styleSet);

        if (Root == null)
            return;

        // Load @font-face fonts before layout so custom families are available.
        LoadFontFacesFromStyleSet(baseUrl);

        // Resolve font-variant-alternates + @font-feature-values + @font-face
        // feature defaults into each box's effective font-feature-settings.
        ResolveFontFeatureValues(Root);

        _selectionHandler = _handlerFactory.CreateSelectionHandler(Root);
        _imageDownloader = new ImageDownloader();
    }

    private void BuildBoundDocument()
    {
        if (_boundDocument == null)
            return;

        DisposeRenderTree();
        var baseUri = new Uri(BaseUrl ?? "/", UriKind.RelativeOrAbsolute);
        DomParser parser = new(new StylesheetLoadHandler(this));
        InitialiseRoot(
            _boundBaseStyleSet,
            BaseUrl,
            (ref HtmlStyleSet styleSet) => parser.GenerateCssTree(_boundDocument, this, ref styleSet, baseUri));
        _boundDocumentVersion = _boundDocument.Version;
    }

    private void EnsureBoundDocumentCurrent()
    {
        if (_boundDocument != null && _boundDocumentVersion != _boundDocument.Version)
            BuildBoundDocument();
    }

    /// <summary>
    /// Iterates shared-model <c>@font-face</c> rules and
    /// loads each font (TrueType/OpenType or WOFF) via the platform adapter.
    /// <c>src</c> URLs are resolved against <paramref name="baseUrl"/> and may be
    /// local files or HTTP(S) resources (e.g. the WPT server serves fonts over
    /// http); remote sources are fetched with a short timeout.
    /// </summary>
    private void LoadFontFacesFromStyleSet(string baseUrl)
    {
        var fontFaces = RendererStyleQueries.GetFontFaces(_styleSet.StyleSheet);
        if (fontFaces.Count == 0)
            return;

        foreach (var face in fontFaces)
        {
            if (string.IsNullOrEmpty(face.Source) || string.IsNullOrEmpty(face.Family))
                continue;

            var src = face.Source.Trim('\'', '"');

            string resolvedFile = ResolveLocalFontPath(src, baseUrl);
            if (!string.IsNullOrEmpty(resolvedFile) && File.Exists(resolvedFile))
            {
                Adapter.LoadFontFromFile(resolvedFile, face.Family);
                continue;
            }

            // Remote source: resolve to an absolute HTTP(S) URL and fetch it.
            if (TryResolveHttpFontUrl(src, baseUrl, out Uri fontUri))
                TryLoadRemoteFont(fontUri, face.Family);
        }
    }

    private static bool TryResolveHttpFontUrl(string src, string baseUrl, out Uri fontUri)
    {
        fontUri = null;
        if (Uri.TryCreate(src, UriKind.Absolute, out var abs) && IsHttp(abs))
        {
            fontUri = abs;
            return true;
        }

        if (!string.IsNullOrEmpty(baseUrl)
            && Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri) && IsHttp(baseUri)
            && Uri.TryCreate(baseUri, src, out var combined) && IsHttp(combined))
        {
            fontUri = combined;
            return true;
        }

        return false;
    }

    private static bool IsHttp(Uri uri) =>
        uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps;

    private void TryLoadRemoteFont(Uri fontUri, string family)
    {
        string tempPath = null;
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            byte[] bytes = client.GetByteArrayAsync(fontUri).GetAwaiter().GetResult();
            if (bytes == null || bytes.Length == 0)
                return;

            // The adapter parses fonts from a file path; stage the downloaded
            // bytes in a temp file (TrueTypeFont/WOFF decoding handles the rest).
            tempPath = Path.Combine(Path.GetTempPath(), "broiler-font-" + Guid.NewGuid().ToString("N") + ".bin");
            File.WriteAllBytes(tempPath, bytes);
            Adapter.LoadFontFromFile(tempPath, family);
        }
        catch
        {
            // Network/parse failure → leave the family unresolved (falls back).
        }
        finally
        {
            try { if (tempPath != null) File.Delete(tempPath); }
            catch { /* best-effort cleanup */ }
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

    /// <summary>
    /// Walks the box tree resolving CSS <c>font-variant-alternates</c> and
    /// <c>@font-face</c> feature defaults into each box's effective
    /// <c>font-feature-settings</c> (normalised to the enabled feature tags),
    /// using <c>@font-feature-values</c> for named feature values.
    /// </summary>
    private void ResolveFontFeatureValues(CssBox box)
    {
        if (box == null)
            return;

        ResolveBoxFontFeatures(box);
        foreach (var child in box.Boxes)
            ResolveFontFeatureValues(child);
    }

    private void ResolveBoxFontFeatures(CssBox box)
    {
        string fva = box.FontVariantAlternates;
        bool hasAlternates = !string.IsNullOrWhiteSpace(fva) && fva.Trim() != "normal";

        // The first (specified) family the element uses, unescaped for matching.
        string family = box.FontFamily ?? string.Empty;
        int comma = family.IndexOf(',');
        if (comma >= 0)
            family = family.Substring(0, comma);
        family = RendererStyleQueries.UnescapeIdentifier(family.Trim().Trim('"', '\''));

        // @font-face feature defaults declared for this family.
        string faceFeatures = null;
        foreach (var face in RendererStyleQueries.GetFontFaces(_styleSet.StyleSheet))
                if (!string.IsNullOrEmpty(face.FeatureSettings)
                    && string.Equals(face.Family, family, StringComparison.OrdinalIgnoreCase))
                    faceFeatures = face.FeatureSettings;

        if (faceFeatures == null && !hasAlternates)
            return; // nothing to merge — keep the element's own font-feature-settings

        var enabled = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        ApplyFeatureSettings(enabled, faceFeatures);
        ApplyFeatureSettings(enabled, box.FontFeatureSettings);
        if (hasAlternates)
            ApplyFontVariantAlternates(enabled, fva, family);

        var sb = new System.Text.StringBuilder();
        foreach (var kv in enabled)
        {
            if (!kv.Value)
                continue;
            if (sb.Length > 0)
                sb.Append(", ");
            sb.Append('"').Append(kv.Key).Append('"');
        }
        box.FontFeatureSettings = sb.Length > 0 ? sb.ToString() : null;
    }

    private static void ApplyFeatureSettings(Dictionary<string, bool> enabled, string settings)
    {
        if (string.IsNullOrWhiteSpace(settings) || settings.Trim() == "normal")
            return;

        foreach (var part in settings.Split(','))
        {
            var item = part.Trim();
            if (item.Length == 0)
                continue;

            string tag, flag;
            int q = item.IndexOf('"');
            int qa = item.IndexOf('\'');
            char quote = q >= 0 ? '"' : (qa >= 0 ? '\'' : '\0');
            if (quote != '\0')
            {
                int st = item.IndexOf(quote);
                int en = item.IndexOf(quote, st + 1);
                if (en <= st)
                    continue;
                tag = item.Substring(st + 1, en - st - 1).Trim();
                flag = item.Substring(en + 1).Trim();
            }
            else
            {
                var sp = item.Split([' ', '\t'], 2, StringSplitOptions.RemoveEmptyEntries);
                tag = sp[0];
                flag = sp.Length > 1 ? sp[1].Trim() : string.Empty;
            }

            if (tag.Length != 4)
                continue;
            bool on = flag.Length == 0
                || flag.Equals("on", StringComparison.OrdinalIgnoreCase)
                || flag == "1"
                || (int.TryParse(flag, out int v) && v != 0);
            enabled[tag] = on; // later declarations win (incl. turning off)
        }
    }

    private void ApplyFontVariantAlternates(Dictionary<string, bool> enabled, string value, string family)
    {
        var featureValues = RendererStyleQueries.GetFontFeatureValues(_styleSet.StyleSheet);
        if (!featureValues.TryGetValue(family, out var typeMap))
            return;

        int i = 0;
        while (i < value.Length)
        {
            int open = value.IndexOf('(', i);
            if (open < 0)
                break;
            int close = value.IndexOf(')', open);
            if (close < 0)
                break;

            int s = open - 1;
            while (s >= 0 && (char.IsLetterOrDigit(value[s]) || value[s] == '-'))
                s--;
            string func = value.Substring(s + 1, open - s - 1).Trim().ToLowerInvariant();
            string args = value.Substring(open + 1, close - open - 1);
            i = close + 1;

            // Map the functional notation to its @font-feature-values type and
            // the OpenType feature-tag prefix (ssNN for styleset, cvNN for
            // character-variant).  Other notations are not yet applied.
            string typeKey;
            string prefix;
            switch (func)
            {
                case "styleset": typeKey = "styleset"; prefix = "ss"; break;
                case "character-variant": typeKey = "character-variant"; prefix = "cv"; break;
                default: continue;
            }

            if (!typeMap.TryGetValue(typeKey, out var nameMap))
                continue;

            foreach (var rawName in args.Split(','))
            {
                string name = RendererStyleQueries.UnescapeIdentifier(rawName.Trim());
                if (name.Length == 0)
                    continue;
                // Value names are case-sensitive (nameMap uses ordinal comparison).
                if (nameMap.TryGetValue(name, out var values))
                    foreach (int v in values)
                        enabled[prefix + v.ToString("00")] = true;
            }
        }
    }

    public void Clear()
    {
        _boundDocument = null;
        _boundBaseStyleSet = null;
        DisposeRenderTree();
    }

    private void DisposeRenderTree()
    {
        if (Root == null)
            return;

        Root.Dispose();
        Root = null;

        _selectionHandler?.Dispose();
        _selectionHandler = null;

        _imageDownloader?.Dispose();
        _imageDownloader = null;

    }

    public void ClearSelection()
    {
        if (_selectionHandler == null)
            return;

        _selectionHandler.ClearSelection();
        RequestRefresh(false);
    }

    public string GetHtml(HtmlGenerationStyle styleGen = HtmlGenerationStyle.Inline)
    {
        EnsureBoundDocumentCurrent();
        return DomUtils.GenerateHtml(Root, styleGen);
    }

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
        EnsureBoundDocumentCurrent();

        var box = DomUtils.GetBoxById(Root, elementId.ToLower());
        return box != null ? CommonUtils.GetFirstValueOrDefault(box.Rectangles, box.Bounds) : null;
    }

    /// <summary>
    /// Returns box geometry for every laid-out box that originated from a canonical
    /// <see cref="Broiler.Dom.DomElement"/> (the <c>SetDocument</c> path), keyed by
    /// that element. Call after <see cref="PerformLayout(RGraphics)"/>. Anonymous
    /// boxes and boxes from the legacy HTML-string parse path (no
    /// <c>SourceElement</c>) are skipped; when an element maps to several boxes the
    /// first encountered in document order wins.
    /// </summary>
    public IReadOnlyDictionary<Broiler.Dom.DomElement, BoxGeometry> CollectLayoutGeometry()
    {
        var result = new Dictionary<Broiler.Dom.DomElement, BoxGeometry>();
        if (Root != null)
            CollectLayoutGeometry(Root, result);
        return result;
    }

    private static void CollectLayoutGeometry(
        CssBox box, Dictionary<Broiler.Dom.DomElement, BoxGeometry> result)
    {
        if (box.SourceElement is { } element && !result.ContainsKey(element))
        {
            var paddingBox = RectangleF.FromLTRB(
                (float)(box.Location.X + box.ActualBorderLeftWidth),
                (float)(box.Location.Y + box.ActualBorderTopWidth),
                (float)(box.ActualRight - box.ActualBorderRightWidth),
                (float)(box.ActualBottom - box.ActualBorderBottomWidth));
            result[element] = new BoxGeometry(box.Bounds, paddingBox, box.ClientRectangle);
        }

        foreach (var child in box.Boxes)
            CollectLayoutGeometry(child, result);
    }

    public void PerformLayout(RGraphics g)
    {
        ArgumentNullException.ThrowIfNull(g);
        EnsureBoundDocumentCurrent();

        ActualSize = SizeF.Empty;
        if (Root == null)
            return;

        // Set viewport dimensions for CSS viewport-relative units (vh, vw, vmin, vmax).
        // MaxSize represents the actual rendering viewport when set; PageSize is the
        // fallback (may be 99999 in auto-size scenarios).
        float vpW = MaxSize.Width > 0 ? Math.Min(MaxSize.Width, PageSize.Width) : PageSize.Width;
        float vpH = MaxSize.Height > 0 ? Math.Min(MaxSize.Height, PageSize.Height) : PageSize.Height;
        // Phase 3.2 dual-run: layout now resolves lengths via the Broiler.CSS port,
        // which keeps its own viewport ThreadStatic — sync it from the same source.
        CssLengthParser.SetViewportSize(vpW, vpH);

        // if width is not restricted we set it to large value to get the actual later
        // CSS2.1 §10.5: Percentage heights on the root element resolve against
        // the initial containing block, whose height is the viewport height.
        // Set the root box's height to the viewport height so that
        // html { height: 100% } resolves correctly.
        float rootH = MaxSize.Height > 0 ? vpH : 0;
        Root.Size = new SizeF(MaxSize.Width > 0 ? MaxSize.Width : 99999, rootH);
        Root.Location = Location;
        // Reuse the container-owned environment bound when the root was created
        // (so font/colour resolve through it even before this pass); just refresh
        // the per-pass graphics surface used by text measurement.
        var layoutEnvironment = Root.LayoutEnvironment as HtmlLayoutEnvironment ?? new HtmlLayoutEnvironment(this);
        layoutEnvironment.SetGraphics(g);
        Root.LayoutEnvironment = layoutEnvironment;
        Root.PerformLayout(layoutEnvironment);

        if (MaxSize.Width <= 0.1)
        {
            // in case the width is not restricted we need to double layout, first will find the width so second can layout by it (center alignment)
            Root.Size = new SizeF((int)Math.Ceiling(ActualSize.Width), 0);
            ActualSize = SizeF.Empty;
            Root.PerformLayout(layoutEnvironment);
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

        RectangleF viewport = GetPaintViewport();

        g.PushClip(viewport);

        var displayList = CreateDisplayList(viewport);
        if (displayList.Items.Count > 0)
            RGraphicsRasterBackend.Instance.Render(displayList, g);

        g.PopClip();
    }

    public DisplayList CreateDisplayList() => CreateDisplayList(GetPaintViewport());

    private DisplayList CreateDisplayList(RectangleF viewport)
    {
        if (LatestFragmentTree == null)
        {
            LatestDisplayList = new DisplayList();
            return LatestDisplayList;
        }

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

        // Paint path: Fragment tree → DisplayList. Raster backends can replay this
        // into RGraphics, or other frontends can translate it into their own command list.
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
        return displayList;
    }

    private RectangleF GetPaintViewport()
    {
        if (MaxSize.Height > 0)
            return new RectangleF(Location.X, Location.Y, Math.Min(MaxSize.Width, PageSize.Width), Math.Min(MaxSize.Height, PageSize.Height));

        return new RectangleF(MarginLeft, MarginTop, PageSize.Width, PageSize.Height);
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
            var args = new HtmlLinkClickedEventArgs(ResolveHref(targetUrl ?? string.Empty), (Dictionary<string, string>)link.HtmlTag.Attributes);
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

    RFont IHtmlContainerInt.GetFont(string family, double size, FontStyle style, string fontFeatures) => Adapter.GetFont(family, size, style, fontFeatures);

    Color IHtmlContainerInt.ParseColor(string colorStr) => ParseCssColor(colorStr);

    RImage IHtmlContainerInt.ConvertImage(object image) => Adapter.ConvertImage(image);

    RImage IHtmlContainerInt.ImageFromStream(Stream stream) => Adapter.ImageFromStream(stream);

    RImage IHtmlContainerInt.GetLoadingImage() => Adapter.GetLoadingImage();

    RImage IHtmlContainerInt.GetLoadingFailedImage() => Adapter.GetLoadingFailedImage();

    void IHtmlContainerInt.DownloadImage(Uri uri, string filePath, bool async, Action<Uri, string, Exception, bool> callback)
        => _imageDownloader?.DownloadImage(uri, filePath, async, (imageUri, fp, error, canceled) => callback(imageUri, fp, error, canceled));

    IImageLoadHandler IHtmlContainerInt.CreateImageLoadHandler(ActionInt<RImage, RectangleF, bool> loadCompleteCallback)
        => new ImageLoadHandler(this, loadCompleteCallback);

    HtmlStyleSet IHtmlContainerInt.StyleSet => _styleSet;

    HtmlStyleSet IHtmlContainerInt.DefaultStyleSet => Adapter.DefaultStyleSet;

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

            _styleSet = null;

            Root?.Dispose();
            Root = null;

            _selectionHandler?.Dispose();
            _selectionHandler = null;
        }
        catch
        { }
    }
}
