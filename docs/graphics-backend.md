# Graphics backend and fallback

Broiler.HTML now renders through its Broiler-owned raster path. The external
`BROILER_GRAPHICS_BACKEND=skia` escape hatch and SkiaSharp implementation are gone from
this repository.

## Backend selection

`Broiler.HTML.Image.BGraphicsBackend` exposes the current diagnostic identity:

- `broiler` — the normal Broiler raster pipeline;
- `stub` — an internal, per-thread compatibility fallback used by controlled tests.

There is no public environment-variable switch. The internal override is deliberately
not a production backend-selection API.

`BGraphicsBackend.CurrentId`, `CurrentDisplayName`, and `CurrentLabel` are the stable
diagnostic surface. Test artifacts should record the label so a fallback result cannot
be mistaken for normal raster evidence.

## Compatibility provider

`Broiler.HTML.Image` still defines backend-neutral compatibility interfaces for bitmap
surfaces, canvas operations, paths, fonts, text shaping, paint creation, typeface
resolution, and image adapters. When no provider is registered,
`CompatProvider` loads `Broiler.HTML.Image.Compat` by assembly/type name and installs its
stub provider.

That boundary is a migration seam, not a claim that Skia is still present. The current
`Broiler.HTML.Image.Compat` project has no SkiaSharp package reference and contains
Broiler-owned fallback code plus a bundled fallback font.

## Validation

Changes to backend or compatibility behavior must run:

- image and graphics unit tests;
- deterministic pixel-diff and repeated-render checks;
- SVG, text shaping, font registration/fallback, clipping, gradient, transform, and
  compositing tests;
- WPF and Win32 host smoke tests where applicable; and
- the owned HTML/CSS suite plus the pinned non-JS WPT selection.

The current removal work and its exit gate are tracked in
[the roadmap](roadmap.md#4-retire-the-skia-era-compatibility-seam).
