# Broiler.HTML roadmap

This roadmap contains only unfinished work. The HTML 5.2 harness, CSS-module
registry, generated coverage dashboard, deterministic render oracles, and phases that
created them are implemented. Their historical plans have been removed.

## Current baseline

The checked-in dashboard currently reports:

- 167 active repository-owned cases and no quarantine;
- 176 HTML/CSS coverage items, 175 covered or explicitly classified;
- 131 CSS current-work rows, 130 at their declared static-renderer target oracle;
- one inventory-only parse target for CSS Linked Parameters Level 1; and
- 110 in-scope CSS rows and 21 browser-runtime/aural rows outside the
  static-renderer release target.

Those numbers show that the historical suite campaigns largely closed their declared
smoke targets. The newly inventoried parse row remains open, and the totals do not prove
that every normative section or edge case in HTML or CSS is implemented. The roadmap
therefore treats that row, coverage depth, public-suite evidence, and release hardening
as remaining work.

## 1. Deepen owned conformance cases

Replace broad module-level smoke evidence with focused assertions for behavior most
likely to hide implementation gaps:

- tokenizer and tree-construction error recovery;
- tables, forms, replaced content, and intrinsic sizing;
- cascade layers, scopes, shorthands, custom properties, and invalid-at-computed-value
  behavior;
- flex, grid, fragmentation, writing modes, bidi, ruby, and font fallback;
- paint order, clipping, masks, filters, blending, transforms, and resource failure;
- malformed-input, timeout, memory, and deterministic-repeat cases.
- the declared parse-level case for `css-module-css-link-params-1`, closing the
  remaining inventory-only implementation-depth row.

Each addition should use the narrowest useful oracle (`tokens`, `dom`,
`computedStyle`, `layout`, `displayList`, `resourceLog`, then `render`) and update the
coverage data that generates `docs/compliance.md`.

Exit gate:

- every claimed feature has at least one focused behavioral assertion, not only a
  parser/no-crash or family-wide smoke case;
- `docs/compliance.md` reports no implementation-depth gap against the declared
  target oracle;
- failures identify the owning layer before pixel comparison where possible;
- stress cases have explicit time and memory bounds; and
- repeated runs produce identical references on every claimed backend.

## 2. Expand public-suite evidence

### Non-JavaScript WPT

The repository can inventory the non-JS corpus and run bounded Chromium render/diff
batches. Make the result release-grade:

- pin the WPT revision used for a release;
- measure a documented, representative selection rather than only a small smoke batch;
- keep exclusions machine-readable, narrowly justified, and expiry-reviewed;
- publish pass/fail totals and artifacts for the pinned selection; and
- add another browser oracle only where its rendering differences can be classified
  without turning browser output into an unquestioned reference.

### Parser and legacy suites

Either integrate or make an explicit product-scope decision for each suite still marked
manual/skipped in `docs/compliance.md`:

- html5lib tokenizer/tree-construction tests;
- the CSS 2.1 test suite;
- Acid2/Acid3 assets and reductions relevant to a static renderer.

An explicit exclusion must name the missing harness capability or non-goal; “not yet
integrated” is not a final status.

## 3. Keep the static-renderer boundary honest

Animation timelines, CSSOM/CSSOM View, Typed OM, Font Loading APIs, observers,
worklets, navigation, and other live browser APIs require a runtime, event loop, and
script-visible document. They remain outside Broiler.HTML's static-renderer claim.

If the product adopts those features, create a browser-runtime roadmap in the owning
host/component. Do not silently widen Broiler.HTML's conformance claim or satisfy those
rows with parser-only cases.

## 4. Retire the Skia-era compatibility seam

The SkiaSharp package and `SK*` implementation types are absent from the current
Broiler.HTML source, but the old cutover architecture is not fully retired:
`Broiler.HTML.Image` still reflection-loads `Broiler.HTML.Image.Compat`, and the
`stub` fallback plus compat-provider interfaces retain a package boundary named for the
former migration.

Finish the cutover:

1. verify the complete restore/package graph contains no SkiaSharp runtime or native
   asset;
2. decide which font, text-shaping, paint, path, bitmap, and image-adapter services are
   still required;
3. move required services behind a clearly named Broiler-owned backend contract;
4. remove the reflection-by-assembly-name bootstrap and the stub fallback when no
   supported host or test needs them; and
5. remove or rename `Broiler.HTML.Image.Compat` so package names describe current
   behavior rather than a completed Skia migration.

Exit gate:

- normal rendering and all supported host packages have no Skia dependency;
- fallback behavior is explicit and tested rather than discovered by reflection;
- raster, text, SVG, image, WPF, pixel-diff, Acid, and WPT gates pass; and
- `docs/graphics-backend.md` and package metadata describe the final topology.

## 5. Release and review gates

Before a preview release:

1. run manifest, integration, coverage, dashboard, implementation-depth, repeatability,
   and full owned-suite checks;
2. run the pinned public-suite selection and publish machine-readable totals;
3. review every quarantine and exclusion;
4. test the supported OS/backend matrix; and
5. update `HUMAN_REVIEW.md` for the exact revision and reviewed scope.

`docs/compliance.md` remains the status source of truth. This file should contain only
work that has not cleared those gates.
