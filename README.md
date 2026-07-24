# Broiler.HTML

Broiler.HTML is a modular .NET HTML renderer split into focused assemblies for parsing, CSS processing, layout, painting, image generation, and WPF hosting.

> **Preview status:** APIs, rendering behavior, and platform support are unstable.
> Substantial implementation work was AI-assisted. Human-review approval is
> revision-scoped; consult [HUMAN_REVIEW.md](HUMAN_REVIEW.md) for the reviewed
> revision and conditions before describing the current checkout as approved.

## Origin, independence, and development

Broiler.HTML is derived in part from
[HTML Renderer](https://github.com/ArthurHub/HTML-Renderer), created by José Manuel
Menéndez Poo and developed by Arthur Teplitzki and other contributors. That project
provided the original renderer architecture and implementation from which Broiler.HTML
evolved. Inherited material remains subject to the BSD 3-Clause License and retained
copyright notices.

Broiler.HTML has since been reorganized into separate parsing, CSS, layout, graphics,
image, orchestration, and platform assemblies, with substantial new and modified code.
Much of that later work was created with AI coding tools under maintainer direction.

Broiler.HTML is maintained independently. It is not an official version, continuation, or
release of HTML Renderer, and the upstream authors have not reviewed or endorsed it. See
[THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) for provenance and license details.

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
- `Broiler.HTML.Graphics` - Broiler.Graphics bitmap and render-list frontend
- `Broiler.HTML.Graphics.Win32.Demo` - simple Win32 URL rendering demo using `Broiler.Graphics.Direct2D`
- `Broiler.HTML.Image` / `Broiler.HTML.Image.Compat` - image rendering,
  deterministic comparison, and the remaining backend-neutral compatibility seam
- `Broiler.HTML.WPF` - WPF rendering surface and controls
- `Broiler.HTML` - shared public surface used by platform adapters

## Public API highlights

- `Broiler.HTML.Image.HtmlRender` renders HTML to in-memory bitmaps, PNG bytes, and files.
- `Broiler.HTML.Graphics.HtmlRender` renders HTML to `Broiler.Graphics.BBitmap` instances, encoded bytes, files, and renderer command lists without WPF.
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
The current solution does not contain checked-in .NET test projects yet, so `dotnet test` currently acts as a repository validation command rather than a substantive automated renderer test suite. The checked-in automated tests currently live under `scripts/wpt/*.test.mjs`.

Run those script tests from the repository root:

```bash
npm test
```

To focus on one file while iterating locally:

```bash
node --test scripts/wpt/run-non-js.test.mjs
```

To debug a specific test by name:

```bash
node --test --test-name-pattern="collectCandidates" scripts/wpt/run-non-js.test.mjs
```

When adding new tests, keep them next to the script they cover, prefer deterministic temp-directory fixtures over networked inputs, and cover both happy paths and argument-validation / failure-mode branches so CI catches regressions in the WPT tooling early.

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
- can inventory the full non-JS corpus without rendering via `--scan-only`
- can apply the checked-in exclusion manifest at `scripts/wpt/non-js-exclusions.json`
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
npm run wpt:run -- --wpt-root ./artifacts/wpt-source --include css/CSS2 --include css/css-backgrounds --include css/css-text --include html/rendering --limit-per-include 2 --width 800 --height 600
```

Inventory the full discoverable non-JS corpus, apply the documented exclusions, and write a machine-readable summary without rendering every case:

```bash
npm run wpt:run -- --wpt-root ./artifacts/wpt-source --exclude-manifest ./scripts/wpt/non-js-exclusions.json --scan-only
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

Use repeated `--include` filters with `--limit-per-include` when you want a bounded smoke batch that still covers several renderer areas instead of consuming the whole limit in the first matching WPT directory.

## Win32 graphics demo

The repository includes a no-WPF/no-WinForms demo that renders a URL with `Broiler.HTML.Graphics` directly into a `Broiler.Graphics.Direct2D` HWND surface:

```bash
dotnet run --project Source/Broiler.HTML.Graphics.Win32.Demo -- https://example.com/
```

The repository also includes a GitHub Actions workflow at `.github/workflows/wpt-non-js.yml`. It checks out the `Broiler.Graphics` submodule, prepares a fresh WPT checkout in CI, inventories the full discoverable non-JS corpus with `--scan-only`, then runs a bounded render/diff sample across CSS2, modern CSS modules, and HTML rendering/semantics directories. Both steps use the same checked-in exclusion manifest so CI, the generated summaries, and the developer documentation stay aligned.

When the workflow records WPT failures and the `ISSUE_TOKEN` secret is configured, the CI job also opens a new GitHub issue for the most common failure signature in that run. The automation uses `ISSUE_TOKEN` only for GitHub Issues API calls that create the issue and expects a fine-grained PAT or GitHub App token with **Issues: Write** access to this repository.

## Documentation

- [Architecture and API notes](docs/architecture.md)
- [Graphics backend and fallback](docs/graphics-backend.md)
- [Compliance suites and status tracking](docs/compliance.md)
- [Current roadmap](docs/roadmap.md)

## Compliance status

Broiler.HTML already contains deterministic render and pixel-diff primitives that can be used to benchmark output against public suites. The tracked suites, current status, and explicit skip reasons are documented in [docs/compliance.md](docs/compliance.md).

Passing tests or compliance cases does not replace source-level human review and is not a
security guarantee. The scoped review record for a release must name the exact reviewed
commit in [HUMAN_REVIEW.md](HUMAN_REVIEW.md).

## License

Broiler.HTML's current project work is licensed under the
[Apache License 2.0](LICENSE). Inherited HTML Renderer material remains subject to the
BSD 3-Clause License included in
[LICENSES/HTML-Renderer-BSD-3-Clause.txt](LICENSES/HTML-Renderer-BSD-3-Clause.txt).
Other dependencies and test data retain their own terms. Redistributors must preserve
the applicable notices. All included licenses disclaim warranties.
