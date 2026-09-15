using System;
using System.Drawing;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Broiler.Graphics.Color;
using Broiler.Graphics.Geometry;
using Broiler.Graphics.Rendering;
using Broiler.Graphics.RenderList;
using Broiler.Graphics.Windowing;
using Broiler.Graphics.Windows;
using Broiler.HTML.Image;

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
            "  Broiler.HTML.Graphics.Win32.Demo.exe https://example.com/\n\n" +
            "Press F5 in the window to reload the page.";

        _ = MessageBox(IntPtr.Zero, usage, "Broiler.HTML.Graphics Win32 Demo", MbIconInformation | MbOk);
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

    internal static void ShowError(IntPtr hwnd, string message) => _ = MessageBox(hwnd, message, "Broiler.HTML.Graphics Win32 Demo", MbIconError | MbOk);

    private const uint MbOk = 0x00000000;
    private const uint MbIconError = 0x00000010;
    private const uint MbIconInformation = 0x00000040;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr dpiContext);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBox(IntPtr hwnd, string text, string caption, uint type);
}

/// <summary>
/// Renders one page into the whole client area.
/// </summary>
/// <remarks>
/// Broiler.Graphics dropped its native edit and button controls after 0.1.0-preview.1, so
/// the address bar this window used to have is gone: the page comes from the command line,
/// and F5 reloads it.
/// </remarks>
[SupportedOSPlatform("windows7.0")]
internal sealed class RenderedUrlWindow : Direct2DWindow
{
    private const int DesiredClientWidth = 1024;
    private const int DesiredClientHeight = 768;
    private const int VirtualKeyF5 = 0x74;

    private readonly string _source;
    private readonly HtmlContainer _container = new();

    // Null whenever the layout has to be redone; BuildRenderList rebuilds it on demand.
    private HtmlGraphicsRenderList? _renderList;
    private bool _hasContent;

    public RenderedUrlWindow(string source) : base(new BWindowOptions
    {
        Title = $"{source} - Broiler.HTML.Graphics Direct2D",
        ClientWidth = DesiredClientWidth,
        ClientHeight = DesiredClientHeight,
        ClearColor = BColor.White,
        RenderOptions = new BRenderOptions(Antialias: true, VSync: true, SubpixelText: true),
    })
    {
        _source = source;
        _container.AvoidAsyncImagesLoading = true;
        _container.AvoidImagesLateLoading = true;
    }

    protected override void OnCreated() => LoadPage();

    protected override void OnKeyDown(BKeyEventArgs e)
    {
        if (e.VirtualKey == VirtualKeyF5)
            LoadPage();
    }

    protected override void OnResized(BSize clientSize, double dpiScale) => DiscardRenderList();

    protected override void OnGraphicsResourcesReleasing() => DiscardRenderList();

    protected override BFrameContext CreateFrameContext(long frameIndex) =>
        new(ResolveClearColor(), frameIndex, Options.RenderOptions);

    protected override BRenderList? BuildRenderList(BSize clientSize)
    {
        if (!_hasContent || clientSize.IsEmpty || Renderer is null)
            return null;

        if (_renderList is not null)
            return _renderList.RenderList;

        var viewport = new RectangleF(0, 0, (float)clientSize.Width, (float)clientSize.Height);
        _container.Location = PointF.Empty;
        _container.MaxSize = viewport.Size;
        _container.PerformLayout(viewport);

        // HtmlContainer.CreateRenderList is gone; the render list is built from a
        // display list now. This is the same call the shipping browser makes in
        // BrowserApp, which is where the working shape was taken from.
        _renderList = HtmlGraphicsRenderListBuilder.Build(
            Renderer,
            _container.CreateDisplayList(),
            viewport);

        return _renderList.RenderList;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            DiscardRenderList();
            _container.Dispose();
        }

        base.Dispose(disposing);
    }

    private void LoadPage()
    {
        try
        {
            string html = Program.LoadHtml(_source, out string baseUrl);
            _container.SetHtmlWithStyleSet(html, baseUrl: baseUrl);
            _hasContent = true;

            DiscardRenderList();
            Invalidate();
        }
        catch (Exception ex)
        {
            Program.ShowError(NativeHandle, ex.Message);
        }
    }

    private void DiscardRenderList()
    {
        _renderList?.Dispose();
        _renderList = null;
    }

    private BColor ResolveClearColor()
    {
        BColor background = _container.GetRootBackgroundColor();
        return !background.IsEmpty && background.A > 0 ? background : BColor.White;
    }
}
