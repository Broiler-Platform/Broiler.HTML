using System;
using System.Drawing;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Broiler.Graphics;
using Broiler.Graphics.Windows;
using Broiler.HTML.Graphics;

namespace Broiler.HTML.Graphics.Win32.Demo;

[SupportedOSPlatform("windows7.0")]
internal static class Program
{
    private const int IdOk = 0;

    [STAThread]
    private static int Main(string[] args)
    {
        _ = SetProcessDpiAwarenessContext(new IntPtr(-4)); // PER_MONITOR_AWARE_V2, best effort.

        if (args.Length > 1 || (args.Length == 1 && IsHelpArgument(args[0])))
        {
            ShowUsage();
            return args.Length == 1 && IsHelpArgument(args[0]) ? IdOk : 1;
        }

        try
        {
            string source = args.Length == 1 ? args[0] : "https://example.com/";
            using var window = new RenderedUrlWindow(source);
            return window.Run();
        }
        catch (Exception ex)
        {
            ShowError(IntPtr.Zero, ex.Message);
            return 1;
        }
    }

    private static bool IsHelpArgument(string argument) =>
        argument is "--help" or "-h" or "-?";

    private static void ShowUsage()
    {
        const string usage =
            "Usage:\n" +
            "  Broiler.HTML.Graphics.Win32.Demo.exe [url]\n\n" +
            "Example:\n" +
            "  Broiler.HTML.Graphics.Win32.Demo.exe https://example.com/";

        MessageBox(IntPtr.Zero, usage, "Broiler.HTML.Graphics Win32 Demo", MbIconInformation | MbOk);
    }

    internal static string LoadHtml(string source, out string baseUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);

        if (Uri.TryCreate(source, UriKind.Absolute, out Uri? uri))
        {
            baseUrl = uri.AbsoluteUri;

            if (uri.IsFile)
                return File.ReadAllText(uri.LocalPath);

            using var client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Broiler.HTML.Graphics.Win32.Demo/1.0");
            using var response = client.GetAsync(uri).GetAwaiter().GetResult();
            response.EnsureSuccessStatusCode();

            if (response.RequestMessage?.RequestUri is { } responseUri)
                baseUrl = responseUri.AbsoluteUri;

            return response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        }

        string path = Path.GetFullPath(source);
        if (!File.Exists(path))
            throw new FileNotFoundException($"The argument is not an absolute URL or an existing file path: {source}", path);

        baseUrl = new Uri(path).AbsoluteUri;
        return File.ReadAllText(path);
    }

    internal static void ShowError(IntPtr hwnd, string message)
    {
        MessageBox(hwnd, message, "Broiler.HTML.Graphics Win32 Demo", MbIconError | MbOk);
    }

    private const uint MbOk = 0x00000000;
    private const uint MbIconError = 0x00000010;
    private const uint MbIconInformation = 0x00000040;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr dpiContext);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBox(IntPtr hwnd, string text, string caption, uint type);
}

[SupportedOSPlatform("windows7.0")]
internal sealed class RenderedUrlWindow : Direct2DWindow
{
    private const int DesiredClientWidth = 1024;
    private const int DesiredClientHeight = 768;
    private const double ToolbarHeight = 44;
    private const double ControlMargin = 8;
    private const double ButtonWidth = 88;
    private const double ControlHeight = 28;

    private readonly string _initialSource;
    private readonly HtmlContainer _container = new();
    private BEditControl? _urlEdit;
    private BButtonControl? _loadButton;
    private HtmlGraphicsRenderList? _renderList;
    private bool _layoutDirty = true;
    private bool _hasContent;

    public RenderedUrlWindow(string source)
        : base(new BWindowOptions
        {
            Title = "Broiler.HTML.Graphics Direct2D",
            ClientWidth = DesiredClientWidth,
            ClientHeight = DesiredClientHeight,
            ClearColor = BColor.White,
            RenderOptions = new BRenderOptions(Antialias: true, VSync: true, SubpixelText: true),
        })
    {
        _initialSource = source;
        _container.AvoidAsyncImagesLoading = true;
        _container.AvoidImagesLateLoading = true;
    }

    protected override void OnCreated()
    {
        _urlEdit = CreateEditControl(new BControlOptions
        {
            Text = _initialSource,
            Bounds = UrlEditBounds(ClientSize),
        });
        _urlEdit.Submitted += (_, _) => LoadFromEdit();

        _loadButton = CreateButtonControl(new BControlOptions
        {
            Text = "Load",
            Bounds = LoadButtonBounds(ClientSize),
        });
        _loadButton.Clicked += (_, _) => LoadFromEdit();

        LayoutControls(ClientSize);
        LoadUrl(_initialSource);
        _urlEdit.Focus();
    }

    protected override void OnResized(BSize clientSize, double dpiScale)
    {
        LayoutControls(clientSize);
        MarkLayoutDirty();
    }

    protected override void OnGraphicsResourcesReleasing()
    {
        MarkLayoutDirty();
    }

    protected override BRect GetRenderBounds(BSize clientSize) =>
        new(0, ToolbarHeight, clientSize.Width, Math.Max(0, clientSize.Height - ToolbarHeight));

    protected override BFrameContext CreateFrameContext(long frameIndex) =>
        new(ResolveClearColor(), frameIndex, Options.RenderOptions);

    protected override BRenderList? BuildRenderList(BSize clientSize)
    {
        if (!_hasContent || clientSize.IsEmpty || Renderer is null)
            return null;

        if (!_layoutDirty && _renderList is not null)
            return _renderList.RenderList;

        var viewport = new RectangleF(0, 0, (float)clientSize.Width, (float)clientSize.Height);
        _container.Location = PointF.Empty;
        _container.MaxSize = viewport.Size;
        _container.PerformLayout(viewport);

        _renderList?.Dispose();
        _renderList = _container.CreateRenderList(Renderer, viewport);
        _layoutDirty = false;

        return _renderList.RenderList;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _urlEdit?.Dispose();
            _urlEdit = null;
            _loadButton?.Dispose();
            _loadButton = null;
            _renderList?.Dispose();
            _renderList = null;
            _container.Dispose();
        }

        base.Dispose(disposing);
    }

    private void LoadFromEdit()
    {
        if (_urlEdit is not null)
            LoadUrl(_urlEdit.Text);
    }

    private void LoadUrl(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
            return;

        SetControlsEnabled(false);
        try
        {
            string html = Program.LoadHtml(source, out string baseUrl);
            _container.SetHtmlWithStyleSet(html, baseUrl: baseUrl);
            _hasContent = true;

            if (_urlEdit is not null && !string.Equals(_urlEdit.Text, source, StringComparison.Ordinal))
                _urlEdit.Text = source;

            MarkLayoutDirty();
            Invalidate();
        }
        catch (Exception ex)
        {
            Program.ShowError(NativeHandle, ex.Message);
        }
        finally
        {
            SetControlsEnabled(true);
        }
    }

    private void SetControlsEnabled(bool enabled)
    {
        if (_urlEdit is not null)
            _urlEdit.Enabled = enabled;
        if (_loadButton is not null)
            _loadButton.Enabled = enabled;
    }

    private void LayoutControls(BSize clientSize)
    {
        if (_urlEdit is null || _loadButton is null)
            return;

        _urlEdit.Bounds = UrlEditBounds(clientSize);
        _loadButton.Bounds = LoadButtonBounds(clientSize);
    }

    private static BRect UrlEditBounds(BSize clientSize)
    {
        double width = Math.Max(1, clientSize.Width - (ControlMargin * 3) - ButtonWidth);
        return new BRect(ControlMargin, ControlMargin, width, ControlHeight);
    }

    private static BRect LoadButtonBounds(BSize clientSize)
    {
        double x = Math.Max(ControlMargin, clientSize.Width - ControlMargin - ButtonWidth);
        return new BRect(x, ControlMargin, ButtonWidth, ControlHeight);
    }

    private void MarkLayoutDirty()
    {
        _layoutDirty = true;
        _renderList?.Dispose();
        _renderList = null;
    }

    private BColor ResolveClearColor()
    {
        BColor background = _container.GetRootBackgroundColor();
        return !background.IsEmpty && background.A > 0
            ? new BColor(background.R, background.G, background.B, background.A)
            : BColor.White;
    }
}
