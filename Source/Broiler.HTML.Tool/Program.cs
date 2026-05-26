using System.Globalization;
using System.Text.Json;
using Broiler.HTML.Core.Core.IR;
using Broiler.HTML.Image;

return HtmlImageRendererCli.Run(args);

internal static class HtmlImageRendererCli
{
    private const int DefaultWidth = 1024;
    private const int DefaultHeight = 768;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public static int Run(string[] args)
    {
        try
        {
            if (args.Length == 0 || IsHelpArgument(args[0]) || string.Equals(args[0], "help", StringComparison.OrdinalIgnoreCase))
            {
                Console.Out.WriteLine(GetGeneralHelpText());
                return 0;
            }

            if (string.Equals(args[0], "render", StringComparison.OrdinalIgnoreCase))
                return RunRender(args[1..]);

            if (string.Equals(args[0], "compare", StringComparison.OrdinalIgnoreCase))
                return RunCompare(args[1..]);

            return RunRender(args);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static int RunRender(string[] args)
    {
        var parseResult = ParseRenderArguments(args);
        if (parseResult.ShowHelp)
        {
            Console.Out.WriteLine(GetRenderHelpText());
            return 0;
        }

        if (parseResult.ErrorMessage is not null)
        {
            Console.Error.WriteLine(parseResult.ErrorMessage);
            Console.Error.WriteLine();
            Console.Error.WriteLine(GetRenderHelpText());
            return 1;
        }

        var options = parseResult.Options!;
        var outputPath = Path.GetFullPath(options.OutputPath!);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? Environment.CurrentDirectory);

        foreach (var font in options.Fonts)
        {
            var fontPath = Path.GetFullPath(font.Path);
            if (!File.Exists(fontPath))
                throw new FileNotFoundException($"Font file not found: {fontPath}", fontPath);

            HtmlRender.LoadFontFromFile(fontPath, font.Alias);
        }

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

    private static int RunCompare(string[] args)
    {
        var parseResult = ParseCompareArguments(args);
        if (parseResult.ShowHelp)
        {
            Console.Out.WriteLine(GetCompareHelpText());
            return 0;
        }

        if (parseResult.ErrorMessage is not null)
        {
            Console.Error.WriteLine(parseResult.ErrorMessage);
            Console.Error.WriteLine();
            Console.Error.WriteLine(GetCompareHelpText());
            return 1;
        }

        var options = parseResult.Options!;
        var actualPath = Path.GetFullPath(options.ActualPath!);
        var baselinePath = Path.GetFullPath(options.BaselinePath!);
        if (!File.Exists(actualPath))
            throw new FileNotFoundException($"Actual image not found: {actualPath}", actualPath);
        if (!File.Exists(baselinePath))
            throw new FileNotFoundException($"Baseline image not found: {baselinePath}", baselinePath);

        using var actual = BBitmap.Decode(actualPath);
        using var baseline = BBitmap.Decode(baselinePath);
        using var diff = PixelDiffRunner.Compare(
            actual,
            baseline,
            DeterministicRenderConfig.Default with
            {
                PixelDiffThreshold = options.PixelDiffThreshold ?? DeterministicRenderConfig.Default.PixelDiffThreshold,
                ColorTolerance = options.ColorTolerance ?? DeterministicRenderConfig.Default.ColorTolerance
            });

        string? diffOutputPath = null;
        if (!diff.IsMatch && diff.DiffBitmap is not null && !string.IsNullOrWhiteSpace(options.DiffOutputPath))
        {
            diffOutputPath = Path.GetFullPath(options.DiffOutputPath);
            Directory.CreateDirectory(Path.GetDirectoryName(diffOutputPath) ?? Environment.CurrentDirectory);
            diff.DiffBitmap.Save(diffOutputPath, BImageFormat.Png);
        }

        var diagnostics = diff.IsMatch
            ? null
            : MismatchClassifier.Classify(diff, actual.Width, actual.Height, baseline.Width, baseline.Height);

        var report = new CompareReport
        {
            ActualPath = actualPath,
            BaselinePath = baselinePath,
            DiffOutputPath = diffOutputPath,
            IsMatch = diff.IsMatch,
            DiffRatio = diff.DiffRatio,
            DiffPixelCount = diff.DiffPixelCount,
            TotalPixelCount = diff.TotalPixelCount,
            PixelDiffThreshold = options.PixelDiffThreshold ?? DeterministicRenderConfig.Default.PixelDiffThreshold,
            ColorTolerance = options.ColorTolerance ?? DeterministicRenderConfig.Default.ColorTolerance,
            ActualWidth = actual.Width,
            ActualHeight = actual.Height,
            BaselineWidth = baseline.Width,
            BaselineHeight = baseline.Height,
            Mismatch = diagnostics is null
                ? null
                : new CompareMismatchReport
                {
                    Category = diagnostics.Category.ToString(),
                    Summary = diagnostics.Summary,
                    AverageChannelDelta = diagnostics.AverageChannelDelta,
                    MaxChannelDelta = diagnostics.MaxChannelDelta,
                    AffectedRows = diagnostics.AffectedRows,
                    AffectedColumns = diagnostics.AffectedColumns
                }
        };

        var json = JsonSerializer.Serialize(report, JsonOptions);
        if (!string.IsNullOrWhiteSpace(options.JsonOutputPath))
        {
            var jsonOutputPath = Path.GetFullPath(options.JsonOutputPath);
            Directory.CreateDirectory(Path.GetDirectoryName(jsonOutputPath) ?? Environment.CurrentDirectory);
            File.WriteAllText(jsonOutputPath, json);
        }

        Console.Out.WriteLine(json);
        return diff.IsMatch ? 0 : 1;
    }

    private static ParseResult<RenderCliOptions> ParseRenderArguments(string[] args)
    {
        if (args.Length == 0)
            return ParseResult<RenderCliOptions>.Help();

        var options = new RenderCliOptions();

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            if (IsHelpArgument(argument))
                return ParseResult<RenderCliOptions>.Help();

            var splitIndex = argument.IndexOf('=');
            var name = splitIndex >= 0 ? argument[..splitIndex] : argument;
            string? value = splitIndex >= 0 ? argument[(splitIndex + 1)..] : null;

            switch (name)
            {
                case "--input":
                case "-i":
                    if (!TryReadValue(args, ref index, ref value, out var inputPath, out var inputError))
                        return ParseResult<RenderCliOptions>.Error(inputError!);
                    options.InputPath = inputPath;
                    break;
                case "--html":
                case "-s":
                    if (!TryReadValue(args, ref index, ref value, out var html, out var htmlError))
                        return ParseResult<RenderCliOptions>.Error(htmlError!);
                    options.Html = html;
                    break;
                case "--output":
                case "-o":
                    if (!TryReadValue(args, ref index, ref value, out var outputPath, out var outputError))
                        return ParseResult<RenderCliOptions>.Error(outputError!);
                    options.OutputPath = outputPath;
                    break;
                case "--format":
                case "-f":
                    if (!TryReadValue(args, ref index, ref value, out var format, out var formatError))
                        return ParseResult<RenderCliOptions>.Error(formatError!);
                    options.Format = format;
                    break;
                case "--base-url":
                    if (!TryReadValue(args, ref index, ref value, out var baseUrl, out var baseUrlError))
                        return ParseResult<RenderCliOptions>.Error(baseUrlError!);
                    options.BaseUrl = baseUrl;
                    break;
                case "--font":
                    if (!TryReadValue(args, ref index, ref value, out var fontRegistration, out var fontError))
                        return ParseResult<RenderCliOptions>.Error(fontError!);
                    options.Fonts.Add(ParseFontRegistration(fontRegistration!));
                    break;
                case "--width":
                case "-w":
                    if (!TryReadInt(args, ref index, ref value, name, positiveOnly: true, out var width, out var widthError))
                        return ParseResult<RenderCliOptions>.Error(widthError!);
                    options.Width = width;
                    break;
                case "--height":
                    if (!TryReadInt(args, ref index, ref value, name, positiveOnly: true, out var height, out var heightError))
                        return ParseResult<RenderCliOptions>.Error(heightError!);
                    options.Height = height;
                    break;
                case "--max-width":
                    if (!TryReadInt(args, ref index, ref value, name, positiveOnly: false, out var maxWidth, out var maxWidthError))
                        return ParseResult<RenderCliOptions>.Error(maxWidthError!);
                    options.MaxWidth = maxWidth;
                    break;
                case "--max-height":
                    if (!TryReadInt(args, ref index, ref value, name, positiveOnly: false, out var maxHeight, out var maxHeightError))
                        return ParseResult<RenderCliOptions>.Error(maxHeightError!);
                    options.MaxHeight = maxHeight;
                    break;
                case "--quality":
                case "-q":
                    if (!TryReadInt(args, ref index, ref value, name, positiveOnly: true, out var quality, out var qualityError))
                        return ParseResult<RenderCliOptions>.Error(qualityError!);
                    if (quality is < 1 or > 100)
                        return ParseResult<RenderCliOptions>.Error("Quality must be between 1 and 100.");
                    options.Quality = quality;
                    break;
                case "--auto-size":
                    options.AutoSize = true;
                    break;
                default:
                    return ParseResult<RenderCliOptions>.Error($"Unknown argument: {argument}");
            }
        }

        if (string.IsNullOrWhiteSpace(options.InputPath) == string.IsNullOrWhiteSpace(options.Html))
            return ParseResult<RenderCliOptions>.Error("Specify exactly one of --input or --html.");

        if (string.IsNullOrWhiteSpace(options.OutputPath))
            return ParseResult<RenderCliOptions>.Error("Missing required --output argument.");

        if (options.AutoSize)
        {
            if (options.Width.HasValue || options.Height.HasValue)
                return ParseResult<RenderCliOptions>.Error("Do not combine --auto-size with --width or --height.");
        }
        else if (options.MaxWidth > 0 || options.MaxHeight > 0)
        {
            return ParseResult<RenderCliOptions>.Error("Use --max-width and --max-height only with --auto-size.");
        }

        return ParseResult<RenderCliOptions>.Success(options);
    }

    private static ParseResult<CompareCliOptions> ParseCompareArguments(string[] args)
    {
        if (args.Length == 0)
            return ParseResult<CompareCliOptions>.Help();

        var options = new CompareCliOptions();

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            if (IsHelpArgument(argument))
                return ParseResult<CompareCliOptions>.Help();

            var splitIndex = argument.IndexOf('=');
            var name = splitIndex >= 0 ? argument[..splitIndex] : argument;
            string? value = splitIndex >= 0 ? argument[(splitIndex + 1)..] : null;

            switch (name)
            {
                case "--actual":
                case "-a":
                    if (!TryReadValue(args, ref index, ref value, out var actualPath, out var actualError))
                        return ParseResult<CompareCliOptions>.Error(actualError!);
                    options.ActualPath = actualPath;
                    break;
                case "--baseline":
                case "-b":
                    if (!TryReadValue(args, ref index, ref value, out var baselinePath, out var baselineError))
                        return ParseResult<CompareCliOptions>.Error(baselineError!);
                    options.BaselinePath = baselinePath;
                    break;
                case "--diff-output":
                    if (!TryReadValue(args, ref index, ref value, out var diffOutput, out var diffError))
                        return ParseResult<CompareCliOptions>.Error(diffError!);
                    options.DiffOutputPath = diffOutput;
                    break;
                case "--json-output":
                    if (!TryReadValue(args, ref index, ref value, out var jsonOutput, out var jsonError))
                        return ParseResult<CompareCliOptions>.Error(jsonError!);
                    options.JsonOutputPath = jsonOutput;
                    break;
                case "--pixel-diff-threshold":
                    if (!TryReadDouble(args, ref index, ref value, name, out var pixelDiffThreshold, out var pixelDiffThresholdError))
                        return ParseResult<CompareCliOptions>.Error(pixelDiffThresholdError!);
                    if (pixelDiffThreshold is < 0 or > 1)
                        return ParseResult<CompareCliOptions>.Error("Pixel diff threshold must be between 0 and 1.");
                    options.PixelDiffThreshold = pixelDiffThreshold;
                    break;
                case "--color-tolerance":
                    if (!TryReadInt(args, ref index, ref value, name, positiveOnly: false, out var colorTolerance, out var colorToleranceError))
                        return ParseResult<CompareCliOptions>.Error(colorToleranceError!);
                    if (colorTolerance is < 0 or > 255)
                        return ParseResult<CompareCliOptions>.Error("Color tolerance must be between 0 and 255.");
                    options.ColorTolerance = colorTolerance;
                    break;
                default:
                    return ParseResult<CompareCliOptions>.Error($"Unknown argument: {argument}");
            }
        }

        if (string.IsNullOrWhiteSpace(options.ActualPath))
            return ParseResult<CompareCliOptions>.Error("Missing required --actual argument.");

        if (string.IsNullOrWhiteSpace(options.BaselinePath))
            return ParseResult<CompareCliOptions>.Error("Missing required --baseline argument.");

        return ParseResult<CompareCliOptions>.Success(options);
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

    private static bool TryReadDouble(string[] args, ref int index, ref string? currentValue, string argumentName, out double value, out string? error)
    {
        if (!TryReadValue(args, ref index, ref currentValue, out var rawValue, out error))
        {
            value = 0;
            return false;
        }

        if (!double.TryParse(rawValue, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out value))
        {
            error = $"Invalid numeric value for {argumentName}: {rawValue}";
            return false;
        }

        error = null;
        return true;
    }

    private static FontRegistration ParseFontRegistration(string rawValue)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rawValue);

        var separatorIndex = rawValue.IndexOf('=');
        if (separatorIndex <= 0)
            return new FontRegistration(rawValue.Trim(), null);

        var alias = rawValue[..separatorIndex].Trim();
        var path = rawValue[(separatorIndex + 1)..].Trim();
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException($"Invalid font registration: {rawValue}");

        return new FontRegistration(path, string.IsNullOrWhiteSpace(alias) ? null : alias);
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

    private static string GetGeneralHelpText() =>
        $$"""
        Usage:
          broiler-html-render [render] --input ./page.html --output ./page.png [--width 1280 --height 720]
          broiler-html-render compare --actual ./broiler.png --baseline ./chromium.png [--diff-output ./diff.png]

        Commands:
          render   Render HTML to an image. This is the default command.
          compare  Compare two rendered images and optionally emit a diff/report.

        Render help:
        {{GetRenderHelpText()}}

        Compare help:
        {{GetCompareHelpText()}}
        """;

    private static string GetRenderHelpText() =>
        """
        Usage:
          broiler-html-render [render] --input ./page.html --output ./page.png [--width 1280 --height 720]
          broiler-html-render [render] --html "<html><body>Hello</body></html>" --output ./page.jpg --format jpeg
          broiler-html-render [render] --input ./page.html --output ./page.png --auto-size [--max-width 1280] [--max-height 0]

        Options:
          -i, --input <path>          Render a local HTML file.
          -s, --html <markup>         Render an HTML string.
          -o, --output <path>         Output image path.
          -f, --format <format>       png, jpg, or jpeg. Defaults to the output extension or png.
              --base-url <url>        Base URL for resolving relative assets.
              --font [Alias=]<path>   Register a TTF/OTF font before rendering. Repeat as needed.
          -w, --width <pixels>        Output width for fixed-size rendering. Defaults to 1024.
              --height <pixels>       Output height for fixed-size rendering. Defaults to 768.
              --auto-size             Size the image to the rendered content instead of a fixed viewport.
              --max-width <px>        Maximum width used with --auto-size. 0 means unbounded.
              --max-height <px>       Maximum height used with --auto-size. 0 means unbounded.
          -q, --quality <1-100>       JPEG quality. Ignored for PNG. Defaults to 90.
          -h, --help                  Show help.
        """;

    private static string GetCompareHelpText() =>
        """
        Usage:
          broiler-html-render compare --actual ./broiler.png --baseline ./chromium.png [--diff-output ./diff.png] [--json-output ./report.json]

        Options:
          -a, --actual <path>              Path to the Broiler-rendered image.
          -b, --baseline <path>            Path to the reference image.
              --diff-output <path>         Where to write the visual diff image when the comparison fails.
              --json-output <path>         Where to write a JSON report.
              --pixel-diff-threshold <n>   Match threshold from 0.0 to 1.0. Defaults to 0.01.
              --color-tolerance <n>        Per-channel color tolerance from 0 to 255. Defaults to 5.
          -h, --help                       Show help.
        """;

    private sealed class RenderCliOptions
    {
        public string? InputPath { get; set; }

        public string? Html { get; set; }

        public string? OutputPath { get; set; }

        public string? Format { get; set; }

        public string? BaseUrl { get; set; }

        public List<FontRegistration> Fonts { get; } = [];

        public int? Width { get; set; }

        public int? Height { get; set; }

        public int MaxWidth { get; set; }

        public int MaxHeight { get; set; }

        public int Quality { get; set; } = 90;

        public bool AutoSize { get; set; }
    }

    private sealed class CompareCliOptions
    {
        public string? ActualPath { get; set; }

        public string? BaselinePath { get; set; }

        public string? DiffOutputPath { get; set; }

        public string? JsonOutputPath { get; set; }

        public double? PixelDiffThreshold { get; set; }

        public int? ColorTolerance { get; set; }
    }

    private sealed record FontRegistration(string Path, string? Alias);

    private sealed class CompareReport
    {
        public required string ActualPath { get; init; }

        public required string BaselinePath { get; init; }

        public string? DiffOutputPath { get; init; }

        public required bool IsMatch { get; init; }

        public required double DiffRatio { get; init; }

        public required int DiffPixelCount { get; init; }

        public required int TotalPixelCount { get; init; }

        public required double PixelDiffThreshold { get; init; }

        public required int ColorTolerance { get; init; }

        public required int ActualWidth { get; init; }

        public required int ActualHeight { get; init; }

        public required int BaselineWidth { get; init; }

        public required int BaselineHeight { get; init; }

        public CompareMismatchReport? Mismatch { get; init; }
    }

    private sealed class CompareMismatchReport
    {
        public required string Category { get; init; }

        public required string Summary { get; init; }

        public required double AverageChannelDelta { get; init; }

        public required int MaxChannelDelta { get; init; }

        public required int AffectedRows { get; init; }

        public required int AffectedColumns { get; init; }
    }

    private sealed class ParseResult<T> where T : class
    {
        private ParseResult(T? options, string? errorMessage, bool showHelp)
        {
            Options = options;
            ErrorMessage = errorMessage;
            ShowHelp = showHelp;
        }

        public T? Options { get; }

        public string? ErrorMessage { get; }

        public bool ShowHelp { get; }

        public static ParseResult<T> Success(T options) => new(options, null, false);

        public static ParseResult<T> Error(string errorMessage) => new(null, errorMessage, false);

        public static ParseResult<T> Help() => new(null, null, true);
    }
}
