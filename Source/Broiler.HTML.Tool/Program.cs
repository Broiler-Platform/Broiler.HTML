using System.Globalization;
using Broiler.HTML.Image;

return HtmlImageRendererCli.Run(args);

internal static class HtmlImageRendererCli
{
    private const int DefaultWidth = 1024;
    private const int DefaultHeight = 768;

    public static int Run(string[] args)
    {
        try
        {
            var parseResult = ParseArguments(args);
            if (parseResult.ShowHelp)
            {
                Console.Out.WriteLine(GetHelpText());
                return 0;
            }

            if (parseResult.ErrorMessage is not null)
            {
                Console.Error.WriteLine(parseResult.ErrorMessage);
                Console.Error.WriteLine();
                Console.Error.WriteLine(GetHelpText());
                return 1;
            }

            var options = parseResult.Options!;
            var outputPath = Path.GetFullPath(options.OutputPath!);
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? Environment.CurrentDirectory);

            var html = options.Html;
            var baseUrl = options.BaseUrl;
            if (options.InputPath is not null)
            {
                var inputPath = Path.GetFullPath(options.InputPath);
                html = File.ReadAllText(inputPath);
                baseUrl ??= new Uri(inputPath).AbsoluteUri;
            }

            var format = ResolveFormat(options.Format, outputPath);
            if (options.AutoSize)
            {
                HtmlRender.RenderToFileAutoSized(
                    html!,
                    outputPath,
                    options.MaxWidth,
                    options.MaxHeight,
                    format,
                    options.Quality,
                    baseUrl: baseUrl);
            }
            else
            {
                HtmlRender.RenderToFile(
                    html!,
                    options.Width ?? DefaultWidth,
                    options.Height ?? DefaultHeight,
                    outputPath,
                    format,
                    options.Quality,
                    baseUrl: baseUrl);
            }

            Console.Out.WriteLine($"Rendered {format.ToString().ToUpperInvariant()} image to {outputPath}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static ParseResult ParseArguments(string[] args)
    {
        if (args.Length == 0)
            return ParseResult.Help();

        var options = new CliOptions();

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            if (IsHelpArgument(argument))
                return ParseResult.Help();

            var splitIndex = argument.IndexOf('=');
            var name = splitIndex >= 0 ? argument[..splitIndex] : argument;
            string? value = splitIndex >= 0 ? argument[(splitIndex + 1)..] : null;

            switch (name)
            {
                case "--input":
                case "-i":
                    if (!TryReadValue(args, ref index, ref value, out var inputPath, out var inputError))
                        return ParseResult.Error(inputError!);
                    options.InputPath = inputPath;
                    break;
                case "--html":
                case "-s":
                    if (!TryReadValue(args, ref index, ref value, out var html, out var htmlError))
                        return ParseResult.Error(htmlError!);
                    options.Html = html;
                    break;
                case "--output":
                case "-o":
                    if (!TryReadValue(args, ref index, ref value, out var outputPath, out var outputError))
                        return ParseResult.Error(outputError!);
                    options.OutputPath = outputPath;
                    break;
                case "--format":
                case "-f":
                    if (!TryReadValue(args, ref index, ref value, out var format, out var formatError))
                        return ParseResult.Error(formatError!);
                    options.Format = format;
                    break;
                case "--base-url":
                    if (!TryReadValue(args, ref index, ref value, out var baseUrl, out var baseUrlError))
                        return ParseResult.Error(baseUrlError!);
                    options.BaseUrl = baseUrl;
                    break;
                case "--width":
                case "-w":
                    if (!TryReadInt(args, ref index, ref value, name, positiveOnly: true, out var width, out var widthError))
                        return ParseResult.Error(widthError!);
                    options.Width = width;
                    break;
                case "--height":
                    if (!TryReadInt(args, ref index, ref value, name, positiveOnly: true, out var height, out var heightError))
                        return ParseResult.Error(heightError!);
                    options.Height = height;
                    break;
                case "--max-width":
                    if (!TryReadInt(args, ref index, ref value, name, positiveOnly: false, out var maxWidth, out var maxWidthError))
                        return ParseResult.Error(maxWidthError!);
                    options.MaxWidth = maxWidth;
                    break;
                case "--max-height":
                    if (!TryReadInt(args, ref index, ref value, name, positiveOnly: false, out var maxHeight, out var maxHeightError))
                        return ParseResult.Error(maxHeightError!);
                    options.MaxHeight = maxHeight;
                    break;
                case "--quality":
                case "-q":
                    if (!TryReadInt(args, ref index, ref value, name, positiveOnly: true, out var quality, out var qualityError))
                        return ParseResult.Error(qualityError!);
                    if (quality is < 1 or > 100)
                        return ParseResult.Error("Quality must be between 1 and 100.");
                    options.Quality = quality;
                    break;
                case "--auto-size":
                    options.AutoSize = true;
                    break;
                default:
                    return ParseResult.Error($"Unknown argument: {argument}");
            }
        }

        if (string.IsNullOrWhiteSpace(options.InputPath) == string.IsNullOrWhiteSpace(options.Html))
            return ParseResult.Error("Specify exactly one of --input or --html.");

        if (string.IsNullOrWhiteSpace(options.OutputPath))
            return ParseResult.Error("Missing required --output argument.");

        if (options.AutoSize)
        {
            if (options.Width.HasValue || options.Height.HasValue)
                return ParseResult.Error("Do not combine --auto-size with --width or --height.");
        }
        else
        {
            if (options.MaxWidth > 0 || options.MaxHeight > 0)
                return ParseResult.Error("Use --max-width and --max-height only with --auto-size.");
        }

        return ParseResult.Success(options);
    }

    private static bool TryReadValue(string[] args, ref int index, ref string? currentValue, out string? value, out string? error)
    {
        if (!string.IsNullOrEmpty(currentValue))
        {
            value = currentValue;
            error = null;
            return true;
        }

        if (index + 1 >= args.Length || args[index + 1].StartsWith('-'))
        {
            value = null;
            error = $"Missing value for {args[index]}.";
            return false;
        }

        value = args[++index];
        error = null;
        return true;
    }

    private static bool TryReadInt(string[] args, ref int index, ref string? currentValue, string argumentName, bool positiveOnly, out int value, out string? error)
    {
        if (!TryReadValue(args, ref index, ref currentValue, out var rawValue, out error))
        {
            value = 0;
            return false;
        }

        if (!int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
        {
            error = $"Invalid integer value for {argumentName}: {rawValue}";
            return false;
        }

        if (positiveOnly ? value <= 0 : value < 0)
        {
            error = positiveOnly
                ? $"{argumentName} must be greater than zero."
                : $"{argumentName} must be zero or greater.";
            return false;
        }

        error = null;
        return true;
    }

    private static BImageFormat ResolveFormat(string? rawFormat, string outputPath)
    {
        if (!string.IsNullOrWhiteSpace(rawFormat))
        {
            return rawFormat.Trim().ToLowerInvariant() switch
            {
                "png" => BImageFormat.Png,
                "jpg" or "jpeg" => BImageFormat.Jpeg,
                _ => throw new ArgumentException($"Unsupported format: {rawFormat}")
            };
        }

        return Path.GetExtension(outputPath).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => BImageFormat.Jpeg,
            _ => BImageFormat.Png,
        };
    }

    private static bool IsHelpArgument(string argument)
        => argument is "--help" or "-h" or "-?";

    private static string GetHelpText() =>
        """
        Usage:
          broiler-html-render --input ./page.html --output ./page.png [--width 1280 --height 720]
          broiler-html-render --html "<html><body>Hello</body></html>" --output ./page.jpg --format jpeg
          broiler-html-render --input ./page.html --output ./page.png --auto-size [--max-width 1280] [--max-height 0]

        Options:
          -i, --input <path>       Render a local HTML file.
          -s, --html <markup>      Render an HTML string.
          -o, --output <path>      Output image path.
          -f, --format <format>    png, jpg, or jpeg. Defaults to the output extension or png.
              --base-url <url>     Base URL for resolving relative assets when using --html.
          -w, --width <pixels>     Output width for fixed-size rendering. Defaults to 1024.
              --height <pixels>    Output height for fixed-size rendering. Defaults to 768.
              --auto-size          Size the image to the rendered content instead of a fixed viewport.
              --max-width <px>     Maximum width used with --auto-size. 0 means unbounded.
              --max-height <px>    Maximum height used with --auto-size. 0 means unbounded.
          -q, --quality <1-100>    JPEG quality. Ignored for PNG. Defaults to 90.
          -h, --help               Show help.
        """;

    private sealed class CliOptions
    {
        public string? InputPath { get; set; }

        public string? Html { get; set; }

        public string? OutputPath { get; set; }

        public string? Format { get; set; }

        public string? BaseUrl { get; set; }

        public int? Width { get; set; }

        public int? Height { get; set; }

        public int MaxWidth { get; set; }

        public int MaxHeight { get; set; }

        public int Quality { get; set; } = 90;

        public bool AutoSize { get; set; }
    }

    private sealed class ParseResult
    {
        private ParseResult(CliOptions? options, string? errorMessage, bool showHelp)
        {
            Options = options;
            ErrorMessage = errorMessage;
            ShowHelp = showHelp;
        }

        public CliOptions? Options { get; }

        public string? ErrorMessage { get; }

        public bool ShowHelp { get; }

        public static ParseResult Success(CliOptions options) => new(options, null, false);

        public static ParseResult Error(string errorMessage) => new(null, errorMessage, false);

        public static ParseResult Help() => new(null, null, true);
    }
}
