# Broiler.HTML architecture and API

## Rendering architecture

Broiler.HTML is structured as a pipeline of small assemblies instead of a single monolith:

1. `Broiler.HTML.Orchestration` parses incoming HTML and coordinates document setup.
2. `Broiler.HTML.CSS` parses stylesheets and computes CSS data.
3. `Broiler.HTML.Dom` performs DOM-driven layout work.
4. `Broiler.HTML.Rendering` converts laid-out content into painting operations.
5. `Broiler.HTML.Image` and `Broiler.HTML.WPF` host the output in concrete backends.

Supporting assemblies keep the pipeline reusable:

- `Broiler.HTML.Adapters` defines the backend-neutral contracts.
- `Broiler.HTML.Core` exposes shared entities plus deterministic IR/JSON helpers.
- `Broiler.HTML.Primitives` and `Broiler.HTML.Utils` hold low-level reusable types and helpers.
- `Broiler.HTML.Image.Compat` supplies the compatibility/image backend used for deterministic output and fixture comparison.

## Compliance-oriented surfaces already present in the repo

The current codebase already exposes pieces needed for standards/compliance work:

- deterministic rendering configuration in `Broiler.HTML.Core/Core/IR`
- image rendering entry points in `Broiler.HTML.Image/HtmlRender.cs`
- pixel comparison in `Broiler.HTML.Image/PixelDiffRunner.cs`
- mismatch triage in `Broiler.HTML.Image/MismatchClassifier.cs`
- WPT/Ahem-oriented font loading via `Broiler.HTML.Image.HtmlRender.LoadFontFromFile(...)`

These APIs are the current foundation for public-suite integration and regression tracking.

## Public API summary

### Image rendering

`Broiler.HTML.Image.HtmlRender` is the main non-UI API:

- `RenderToImage(...)`
- `RenderToImageAutoSized(...)`
- `RenderToImageAtAnchor(...)`
- `RenderToPng(...)`
- `RenderToFile(...)`
- `RenderToFileAutoSized(...)`
- `LoadFontFromFile(...)`

Use this assembly when you need deterministic image output, fixture generation, or compliance-image comparisons.

### Visual comparison

`Broiler.HTML.Image.PixelDiffRunner.Compare(...)` performs per-pixel comparison of rendered output against a baseline image and returns a `PixelDiffResult`.

`Broiler.HTML.Image.MismatchClassifier.Classify(...)` converts a failed diff into a more actionable category such as:

- `SizeMismatch`
- `SubpixelAntiAliasing`
- `ColorShift`
- `LayoutShift`
- `MissingContent`
- `MinorDiff`

### WPF hosting

`Broiler.HTML.WPF` exposes the WPF-facing surface:

- `HtmlRender`
- `HtmlContainer`
- `HtmlPanel`
- `HtmlLabel`

Use `HtmlRender` for direct drawing/image generation and `HtmlPanel` / `HtmlLabel` for control-based hosting.

### Backend extension point

`Broiler.HTML.Adapters.RAdapter` is the central abstraction for backend-specific functionality such as brushes, pens, fonts, images, clipboard integration, and context menus.

## Repeatable repository checks

The repository currently supports these repeatable checks directly:

```bash
cd /home/runner/work/Broiler.HTML/Broiler.HTML/Source
dotnet build Broiler.HTML.slnx
dotnet test Broiler.HTML.slnx
```

For visual compliance work, render a fixture with `Broiler.HTML.Image.HtmlRender`, compare it with `PixelDiffRunner`, and classify failures with `MismatchClassifier`.
