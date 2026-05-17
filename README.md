# Broiler.HTML

Broiler.HTML is a modular .NET HTML renderer split into focused assemblies for parsing, CSS processing, layout, painting, image generation, and WPF hosting.

## Repository goals

- keep the renderer modular and maintainable
- document the public API and architecture of the current Broiler.HTML codebase
- track public compliance suites and their status from inside the repository
- make baseline build and compliance checks repeatable

## Solution layout

The solution file is `Source/Broiler.HTML.slnx` and the codebase is organized into these main assemblies:

- `Broiler.HTML.Primitives` - shared primitive types
- `Broiler.HTML.Utils` - common utilities and resource helpers
- `Broiler.HTML.Adapters` - backend-neutral adapter abstractions
- `Broiler.HTML.Core` - core entities and deterministic IR helpers
- `Broiler.HTML.CSS` - CSS parsing and stylesheet handling
- `Broiler.HTML.Dom` - DOM/layout processing
- `Broiler.HTML.Orchestration` - HTML parsing and renderer orchestration
- `Broiler.HTML.Rendering` - paint-time handlers and rendering logic
- `Broiler.HTML.Image` / `Broiler.HTML.Image.Compat` - image rendering, deterministic comparison, and Skia compatibility
- `Broiler.HTML.WPF` - WPF rendering surface and controls
- `Broiler.HTML` - shared public surface used by platform adapters

## Public API highlights

- `Broiler.HTML.Image.HtmlRender` renders HTML to in-memory bitmaps, PNG bytes, and files.
- `Broiler.HTML.Tool` exposes a cross-platform command-line renderer plus image-diff reporting for HTML compliance workflows.
- `Broiler.HTML.Image.PixelDiffRunner` compares rendered output to a baseline image.
- `Broiler.HTML.Image.MismatchClassifier` classifies visual mismatches for compliance triage.
- `Broiler.HTML.WPF.HtmlRender` renders HTML into WPF drawing contexts and images.
- `Broiler.HTML.WPF.HtmlPanel` and `Broiler.HTML.WPF.HtmlLabel` expose WPF controls.
- `Broiler.HTML.Adapters.RAdapter` is the backend extension point for graphics, fonts, images, clipboard, and context-menu services.

## Build and validation

Run the repository build from the solution directory:

```bash
cd Source
dotnet build Broiler.HTML.slnx
dotnet test Broiler.HTML.slnx
```

`Broiler.HTML.WPF` now enables Windows targeting so solution-wide restore/build validation also works on non-Windows CI hosts.
The current solution does not contain checked-in test projects yet, so `dotnet test` currently acts as a repository validation command rather than a substantive automated renderer test suite.

## Command-line HTML renderer

The repository now includes a cross-platform .NET tool project at `Source/Broiler.HTML.Tool` for rendering HTML to PNG or JPEG from Windows, Linux, or macOS.

Run it directly from the repository:

```bash
dotnet run --project Source/Broiler.HTML.Tool -- --input ./page.html --output ./page.png --width 1280 --height 720
```

Render an inline HTML string:

```bash
dotnet run --project Source/Broiler.HTML.Tool -- --html "<html><body><h1>Hello</h1></body></html>" --output ./hello.jpg --format jpeg --width 800 --height 600
```

Auto-size to the rendered content:

```bash
dotnet run --project Source/Broiler.HTML.Tool -- --input ./page.html --output ./page.png --auto-size --max-width 1200
```

The tool supports these standard arguments:

- `--input <path>` or `--html <markup>` (exactly one is required)
- `--output <path>`
- `--format png|jpg|jpeg`
- `--width <pixels>` and `--height <pixels>` for fixed-size rendering
- `--auto-size` with optional `--max-width` / `--max-height`
- `--quality <1-100>` for JPEG output
- `--base-url <url>` to resolve relative assets when rendering inline HTML
- `--font [Alias=]<path>` to register deterministic fixture fonts such as WPT Ahem
- `--help` to display usage

For local HTML files, the CLI automatically uses the source file as the base URL so relative stylesheets and images resolve consistently.

Compare a Broiler render against a reference browser screenshot:

```bash
dotnet run --project Source/Broiler.HTML.Tool -- compare --actual ./broiler.png --baseline ./chromium.png --diff-output ./diff.png --json-output ./report.json
```

## WPT non-JS compliance runner

The repository now includes a Playwright-based runner for local [web-platform-tests](https://github.com/web-platform-tests/wpt) checkouts that:

- discovers HTML/XHTML cases while skipping files that appear to depend on JavaScript
- renders each selected case through the Broiler CLI
- captures a Chromium reference screenshot with JavaScript disabled
- compares the two images with `PixelDiffRunner` / `MismatchClassifier`
- writes per-test diff artifacts plus `summary.json` and `summary.md`

Set it up from the repository root:

```bash
npm install
npm run wpt:install-browsers
npm run wpt:prepare -- --output ./artifacts/wpt-source
```

The prepare step can either clone the official WPT repository or copy an existing local checkout:

```bash
npm run wpt:prepare -- --output ./artifacts/wpt-source --source /path/to/existing/wpt --force
```

Run a focused batch against the prepared WPT tree:

```bash
dotnet build Source/Broiler.HTML.slnx
npm run wpt:run -- --wpt-root ./artifacts/wpt-source --include css/css-backgrounds --limit 20 --width 800 --height 600
```

Each selected WPT case gets a shared timeout budget across Broiler rendering, Chromium capture, and image comparison. The default is 30000 ms, and timed out cases are recorded as failures in the generated summary files instead of hanging the batch indefinitely.

Adjust the timeout for slower local machines or heavier cases with either a CLI flag or an environment variable:

```bash
npm run wpt:run -- --wpt-root ./artifacts/wpt-source --test-timeout-ms 60000
BROILER_WPT_TEST_TIMEOUT_MS=60000 npm run wpt:run -- --wpt-root ./artifacts/wpt-source
```

To analyze the resulting `summary.json` (including older artifacts whose `report.json` files came from the .NET CLI's PascalCase JSON), run:

```bash
python3 ./scripts/wpt/analyze-summary.py ./artifacts/wpt/summary.json --emit-rerun-args
```

The analyzer groups timeout phases, summarizes mismatch categories, and emits focused `--include` arguments for rerunning just the failing cases.

If your selected WPT cases use fixture fonts such as Ahem, pass them through to the Broiler renderer:

```bash
npm run wpt:run -- --wpt-root /path/to/wpt --include css/css-text --font Ahem=/path/to/wpt/fonts/Ahem.ttf
```

The repository also includes a GitHub Actions workflow at `.github/workflows/wpt-non-js.yml`. It prepares a fresh WPT checkout in CI, runs a focused non-JS subset, uploads the diff artifacts, and adds the rendered summary to the workflow summary page.

Recent CI runs showed two main patterns:

- the observed batch timeouts were caused by the Broiler render phase exhausting the 30000 ms per-test budget on a subset of `css/css-backgrounds` cases
- the non-timeout visual mismatches clustered around `MissingContent` for `background-attachment-fixed*` cases, plus a smaller `MinorDiff` in `background-attachment-local-hidden.html`

## Documentation

- [Architecture and API notes](docs/architecture.md)
- [Compliance suites and status tracking](docs/compliance.md)

## Compliance status

Broiler.HTML already contains deterministic render and pixel-diff primitives that can be used to benchmark output against public suites. The tracked suites, current status, and explicit skip reasons are documented in [docs/compliance.md](docs/compliance.md).
