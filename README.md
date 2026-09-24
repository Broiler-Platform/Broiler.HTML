# Broiler.HTML

Broiler.HTML is a modular .NET HTML renderer split into focused assemblies for parsing, CSS processing, layout, painting, and image generation.

> **Preview status:** APIs, rendering behavior, and platform support are unstable.
> Substantial implementation work was AI-assisted. Human-review approval is
> revision-scoped; consult [HUMAN_REVIEW.md](https://github.com/Broiler-Platform/Broiler.HTML/blob/main/HUMAN_REVIEW.md) for the reviewed
> revision and conditions before describing the current checkout as approved.

> **Security warning:** Broiler.HTML is not hardened and is not approved for hostile or
> untrusted HTML/CSS. It may still contain security-sensitive rendering paths (parsing,
> resource resolution, fonts, images, and file or network access through adapters). Use
> it only with controlled inputs; where untrusted content cannot be avoided, sandbox the
> process and restrict resource loading.

## Packages

Preview packages are published to [nuget.org](https://www.nuget.org/packages?q=Broiler.HTML) in lockstep under one `0.1.0-preview.N` version:

| Package | Contents |
| --- | --- |
| `Broiler.HTML.Image` | Render HTML to in-memory bitmaps, PNG bytes, and files |
| `Broiler.HTML.Graphics` | Render HTML to `Broiler.Graphics` bitmaps, encoded images, and render command lists |
| `Broiler.HTML.Image.Compat` | Backend-neutral image adapter with a bundled fallback font |
| `Broiler.HTML.Orchestration` | Box-tree construction, the HTML container, and painting |
| `Broiler.HTML.Dom` | HTML parsing into the Broiler DOM |
| `Broiler.HTML.Core` | Shared entities and handler contracts |

```bash
dotnet add package Broiler.HTML.Image --prerelease
```

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
[THIRD_PARTY_NOTICES.md](https://github.com/Broiler-Platform/Broiler.HTML/blob/main/THIRD_PARTY_NOTICES.md) for provenance and license details.

## Repository goals

- keep the renderer modular and maintainable
- document the public API and architecture of the current Broiler.HTML codebase
- track public compliance suites and their status from inside the repository
- make baseline build and compliance checks repeatable

## Solution layout

The solution file is `Broiler.HTML.slnx` at the repository root and the codebase is organized into these assemblies. CSS, the DOM, layout and graphics come from the Broiler.CSS, Broiler.DOM, Broiler.Layout and Broiler.Graphics packages.

- `Broiler.HTML.Core` - shared entities, handler contracts, and image download and loading
- `Broiler.HTML.Dom` - HTML parsing into the Broiler DOM and the layout environment
- `Broiler.HTML.Orchestration` - box-tree construction, the HTML container, and painting into a display list
- `Broiler.HTML.Graphics` - Broiler.Graphics render-list frontend
- `Broiler.HTML.Graphics.Win32.Demo` - simple Win32 URL rendering demo using the Direct2D backend in `Broiler.Graphics.Windows`
- `Broiler.HTML.Image` / `Broiler.HTML.Image.Compat` - image rendering,
  deterministic comparison, and the remaining backend-neutral compatibility seam

## Public API highlights

- `Broiler.HTML.Image.HtmlRender` renders HTML to in-memory bitmaps, PNG bytes, and files.
- `Broiler.HTML.Graphics.HtmlRender` renders HTML to `Broiler.Graphics.BBitmap` instances, encoded bytes, files, and renderer command lists.
- `Broiler.HTML.Tool` exposes a cross-platform command-line renderer plus image-diff reporting for HTML compliance workflows.
- `Broiler.HTML.Image.PixelDiffRunner` compares rendered output to a baseline image.
- `Broiler.HTML.Image.MismatchClassifier` classifies visual mismatches for compliance triage.
- `Broiler.HTML.Adapters.RAdapter` is the backend extension point for graphics, fonts, images, clipboard, and context-menu services.
- `HtmlContainer.RequestTransport` and `HtmlContainer.DocumentContext` route the container's images, stylesheets and fonts through a host's [Broiler.Net](https://github.com/Broiler-Platform/Broiler.Net) network session; see below.

### Subresource loading and cookies

A browser host gives each container its profile's `IBrowserRequestTransport` (a Broiler.Net `BrowserNetworkSession`) and the `DocumentRequestContext` of the document it renders. Images, `<link>` stylesheets and `@font-face` fonts are then sent through that transport as Fetch subresource requests of that document, so they carry and store the profile's cookies:

| Load | Request |
| --- | --- |
| `<img>`, CSS images | `image`, the element's `crossorigin` attribute (none: no-cors with credentials; `anonymous`: CORS, same-origin credentials; `use-credentials`: CORS with credentials) |
| `<link rel="stylesheet">` | `style`, the element's `crossorigin` attribute; the response must be `text/css` (a quirks-mode document may use a same-origin or CORS response of another type, never one sent with `X-Content-Type-Options: nosniff`); relative `url()` references, quoted or not, resolve against the response's final URL, after redirects |
| `@font-face` | `font`, always CORS with same-origin credentials; relative sources resolve against the document's `<base href>`, then the document URL |

The `StylesheetLoad` and `ImageLoad` events, `data:` URLs, local files and the offline-subresource policy still come first. Local files are read only for a `file:` document, or for a container with no document context and no transport: a web page's stylesheets, images and fonts never come off the file system, nor off a UNC share (`file://host/share`, `\\host\share` and, on Windows, a protocol-relative `//host/share`). Non-2xx responses and transport errors (CORS failures included) are load failures. Budgets are unchanged: 5 s for images and stylesheets, 10 s for fonts. Transport loads skip the shared image cache in `%TEMP%\HtmlRenderer`; the container keeps an in-memory cache instead, kept across reparses of the same document and dropped when the transport or the document context changes. A host that renders one document in several containers (one per painted frame of a page that is still loading, say) shares one cache between them with `HtmlContainer.ShareSubresourceCacheWith`, so each subresource is fetched once. A render tree's loads are cancelled when it is replaced, cleared or the container is disposed. The container never derives a document identity from `BaseUrl`: a transport without a `DocumentContext` loads no network subresources.

Without a transport (`HtmlRender`, the command-line tool, the WPT runner), the renderer loads through process-wide clients that send the Broiler User-Agent and follow redirects as before, but send and store no cookies.

## Build and validation

Run the repository build from the repository root:

```bash
dotnet build Broiler.HTML.slnx
```

The Broiler components this renderer builds on - Broiler.CSS, Broiler.Dom.Html, Broiler.Graphics, Broiler.Layout, Broiler.Media and Broiler.Net - are nuget.org packages pinned in `Directory.Packages.props`. `NuGet.config` restores from nuget.org only, so no credentials are needed.

NuGet never re-downloads a package id and version it already has in its global cache, so a locally packed build of the same version will shadow the published one. Remove the stale entry from `~/.nuget/packages` if a restore resolves types that the feed package does not have.

`tests/Broiler.HTML.Tests` covers subresource loading against loopback HTTP servers: cookies through a Broiler.Net session, CORS, redirects, the cache and cancellation, and the cookie-less legacy path. Run it with:

```bash
dotnet test Broiler.HTML.slnx -c Release
```

The WPT tooling has its own script tests under `scripts/wpt/*.test.mjs`.

### Continuous integration and publishing

`.github/workflows/ci.yml` follows the other Broiler components. On every push to `main` and every pull request it builds `Release` on Ubuntu and Windows, runs the .NET tests, the script tests and the HTML 5.2 corpus consistency checks, then packs and verifies every package on Ubuntu and attaches them as `nuget-packages`.

`.github/workflows/publish.yml` resolves the next free `0.1.0-preview.N`, reruns CI with that version, proves that a consumer can restore the packages with nuget.org as its only feed, and pushes them to nuget.org with the `NUGET_TOKEN` secret. Dispatch it manually (`dry-run` is on by default) or push a `v*` tag. The version is one past the highest preview of any of these packages on nuget.org, and never below the `VersionSuffix` floor in `Directory.Build.props`, which records the `preview.1`-`preview.3` numbers already spent on the retired GitHub Packages feed.

The HTML 5.2 and non-JS WPT render/diff suites are the two manually dispatched workflows beside them.

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
dotnet build Broiler.HTML.slnx
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

The repository includes a demo that renders a URL with `Broiler.HTML.Graphics` directly into a Direct2D HWND surface from `Broiler.Graphics.Windows`. Press F5 in the window to reload the page:

```bash
dotnet run --project Source/Broiler.HTML.Graphics.Win32.Demo -- https://example.com/
```

The repository also includes a manually dispatched GitHub Actions workflow at `.github/workflows/wpt-non-js.yml`. It restores the Broiler packages from nuget.org, prepares a fresh WPT checkout in CI, inventories the full discoverable non-JS corpus with `--scan-only`, then runs a bounded render/diff sample across CSS2, modern CSS modules, and HTML rendering/semantics directories. Both steps use the same checked-in exclusion manifest so CI, the generated summaries, and the developer documentation stay aligned.

When the workflow records WPT failures and the `ISSUE_TOKEN` secret is configured, the CI job also opens a new GitHub issue for the most common failure signature in that run. The automation uses `ISSUE_TOKEN` only for GitHub Issues API calls that create the issue and expects a fine-grained PAT or GitHub App token with **Issues: Write** access to this repository.

## Documentation

- [Architecture and API notes](https://github.com/Broiler-Platform/Broiler.HTML/blob/main/docs/architecture.md)
- [Graphics backend and fallback](https://github.com/Broiler-Platform/Broiler.HTML/blob/main/docs/graphics-backend.md)
- [Compliance suites and status tracking](https://github.com/Broiler-Platform/Broiler.HTML/blob/main/docs/compliance.md)
- [Current roadmap](https://github.com/Broiler-Platform/Broiler.HTML/blob/main/docs/roadmap.md)

## Compliance status

Broiler.HTML already contains deterministic render and pixel-diff primitives that can be used to benchmark output against public suites. The tracked suites, current status, and explicit skip reasons are documented in [docs/compliance.md](https://github.com/Broiler-Platform/Broiler.HTML/blob/main/docs/compliance.md).

Passing tests or compliance cases does not replace source-level human review and is not a
security guarantee. The scoped review record for a release must name the exact reviewed
commit in [HUMAN_REVIEW.md](https://github.com/Broiler-Platform/Broiler.HTML/blob/main/HUMAN_REVIEW.md).

## License

Broiler.HTML's current project work is licensed under the
[Apache License 2.0](https://github.com/Broiler-Platform/Broiler.HTML/blob/main/LICENSE). Inherited HTML Renderer material remains subject to the
BSD 3-Clause License included in
[LICENSES/HTML-Renderer-BSD-3-Clause.txt](https://github.com/Broiler-Platform/Broiler.HTML/blob/main/LICENSES/HTML-Renderer-BSD-3-Clause.txt).
`Broiler.HTML.Image.Compat` embeds the Vazirmatn font under the SIL Open Font License 1.1.
Other dependencies and test data retain their own terms. Redistributors must preserve
the applicable notices. All included licenses disclaim warranties.
