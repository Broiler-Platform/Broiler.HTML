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

        if (args.Length != 1 || IsHelpArgument(args[0]))
        {
            ShowUsage();
            return args.Length == 1 && IsHelpArgument(args[0]) ? IdOk : 1;
        }

        try
        {
            string source = args[0];
            string html = LoadHtml(source, out string baseUrl);

            using var window = new RenderedUrlWindow(source, html, baseUrl);
            return window.Run();
        }
        catch (Exception ex)
        {
            MessageBox(IntPtr.Zero, ex.Message, "Broiler.HTML.Graphics Win32 Demo", MbIconError | MbOk);
            return 1;
        }
    }

    private static bool IsHelpArgument(string argument) =>
        argument is "--help" or "-h" or "-?";

    private static void ShowUsage()
    {
        const string usage =
            "Usage:\n" +
            "  Broiler.HTML.Graphics.Win32.Demo.exe <url>\n\n" +
            "Example:\n" +
            "  Broiler.HTML.Graphics.Win32.Demo.exe https://example.com/";

        MessageBox(IntPtr.Zero, usage, "Broiler.HTML.Graphics Win32 Demo", MbIconInformation | MbOk);
    }

    private static string LoadHtml(string source, out string baseUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);

        if (Uri.TryCreate(source, UriKind.Absolute, out Uri? uri))
        {
            baseUrl = uri.AbsoluteUri;

            if (uri.IsFile)
                return File.ReadAllText(uri.LocalPath);

            using var client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Broiler.HTML.Graphics.Win32.Demo/1.0");
            return client.GetStringAsync(uri).GetAwaiter().GetResult();
        }

        string path = Path.GetFullPath(source);
        if (!File.Exists(path))
            throw new FileNotFoundException($"The argument is not an absolute URL or an existing file path: {source}", path);

        baseUrl = new Uri(path).AbsoluteUri;
        return File.ReadAllText(path);
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

    private readonly HtmlContainer _container = new();
    private HtmlGraphicsRenderList? _renderList;
    private bool _layoutDirty = true;

    public RenderedUrlWindow(string source, string html, string baseUrl)
        : base(new BWindowOptions
        {
            Title = $"Broiler.HTML.Graphics Direct2D - {source}",
            ClientWidth = DesiredClientWidth,
            ClientHeight = DesiredClientHeight,
            ClearColor = BColor.White,
            RenderOptions = new BRenderOptions(Antialias: true, VSync: true, SubpixelText: true),
        })
    {
        _container.AvoidAsyncImagesLoading = true;
        _container.AvoidImagesLateLoading = true;
        _container.SetHtml(html, baseUrl: baseUrl);
    }

    protected override void OnResized(BSize clientSize, double dpiScale)
    {
        MarkLayoutDirty();
    }

    protected override void OnGraphicsResourcesReleasing()
    {
        MarkLayoutDirty();
    }

    protected override BFrameContext CreateFrameContext(long frameIndex) =>
        new(ResolveClearColor(), frameIndex, Options.RenderOptions);

    protected override BRenderList? BuildRenderList(BSize clientSize)
    {
        if (clientSize.IsEmpty || Renderer is null)
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
            _renderList?.Dispose();
            _renderList = null;
            _container.Dispose();
        }

        base.Dispose(disposing);
    }

    private void MarkLayoutDirty()
    {
        _layoutDirty = true;
        _renderList?.Dispose();
        _renderList = null;
    }

    private BColor ResolveClearColor()
    {
        Color background = _container.GetRootBackgroundColor();
        return !background.IsEmpty && background.A > 0
            ? new BColor(background.R, background.G, background.B, background.A)
            : BColor.White;
    }
}
