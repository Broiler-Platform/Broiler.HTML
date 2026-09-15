# Broiler.HTML architecture and API

## Rendering architecture

Broiler.HTML is structured as a pipeline of small assemblies instead of a single monolith:

1. `Broiler.HTML.Dom` parses incoming HTML into the Broiler DOM and hosts the layout environment.
2. `Broiler.HTML.Orchestration` builds the styled box tree with the `Broiler.CSS` cascade, lays it out with `Broiler.Layout`, and paints the laid-out fragments into a display list.
3. `Broiler.HTML.Image` rasterizes a display list into a bitmap, and `Broiler.HTML.Graphics` translates one into a `Broiler.Graphics` render list.

Supporting assemblies keep the pipeline reusable:

- `Broiler.HTML.Core` exposes shared entities, handler contracts, and image download and loading.
- `Broiler.HTML.Image.Compat` supplies the fallback text, font and image services used for deterministic output and fixture comparison.

## Compliance-oriented surfaces already present in the repo

The current codebase already exposes pieces needed for standards/compliance work:

- deterministic rendering configuration in `Broiler.HTML.Core/IR`
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

### Backend extension point

`Broiler.HTML.Adapters.RAdapter` is the central abstraction for backend-specific functionality such as brushes, pens, fonts, images, clipboard integration, and context menus.

The image renderer's current raster/fallback selection and the remaining
`Broiler.HTML.Image.Compat` seam are documented in
[Graphics backend and fallback](graphics-backend.md).

## Repeatable repository checks

The repository currently supports these repeatable checks directly:

```bash
dotnet build Broiler.HTML.slnx
npm test
```

For visual compliance work, render a fixture with `Broiler.HTML.Image.HtmlRender`, compare it with `PixelDiffRunner`, and classify failures with `MismatchClassifier`. The repository-level wrappers for that flow are `Broiler.HTML.Tool compare` and `scripts/wpt/run-non-js.mjs`.
