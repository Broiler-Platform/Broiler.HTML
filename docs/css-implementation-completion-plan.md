# CSS Implementation Completion Plan

Date: 2026-06-21
Target repository: Broiler.HTML
Primary standards inventory: W3C CSS current work page, checked on 2026-06-21
Local registry snapshot: `tests/html52/generated/css-modules/registry.json`

## Purpose

This document plans the technical work needed to move Broiler.HTML from a
static renderer with broad CSS smoke coverage to a deeper, near-complete CSS
engine. It complements `docs/css-modules-test-suite-plan.md`, which covers the
test-suite structure. This plan is about implementation architecture, subsystem
ownership, conformance gates, and how to turn parser/no-crash coverage into
computed-style, layout, display-list, and pixel-level correctness.

The current merged HTML+CSS suite is healthy: 150 active cases pass, all 130
CSS current-work registry rows are covered or classified, and no planned
coverage items are uncovered. That does not mean Broiler.HTML implements every
CSS behavior. Many CSS module rows currently have parser, stress, or no-crash
coverage only. Reaching "100%" requires deeper oracles and engine work.

## Definition Of 100%

CSS has no single version number. The W3C CSS current-work page tracks
completed specifications and drafts by module and level. The local registry
currently records 130 active rows across REC, CRD, CR, WD, and FPWD statuses.

Broiler.HTML needs two explicit targets:

1. Static renderer 100%
   - Every renderer-relevant CSS module row has executable coverage at the
     deepest applicable oracle: parse, cascade, computed style, used value,
     layout, display list, render, resource log, performance, and security.
   - Every unsupported or inapplicable feature is classified with a deterministic
     negative behavior and cannot crash or corrupt later declarations.
   - The engine produces deterministic output across supported platforms.

2. Full CSS platform 100%
   - Everything in static renderer 100%, plus live browser/runtime CSS features:
     CSSOM, CSSOM View, Typed OM, Font Loading API, animations, transitions,
     scroll-driven animations, View Transitions, Resize Observer, Houdini
     worklets, spatial navigation, custom highlights, and event/timeline-driven
     behavior.
   - This target requires more than a renderer. It needs a live document model,
     event loop, viewport/scrolling model, animation timeline, worklet host, and
     script-visible CSS APIs.

Unless the project intentionally becomes a browser runtime, release gates should
use the static renderer 100% target. The full CSS platform target should remain
a separate product decision.

## Current Baseline

As of this plan:

- Comprehensive merged suite: 150 cases, 150 passing.
- CSS module generated cases: 29.
- CSS current-work registry rows: 130.
- CSS module current oracle depth: 20 computed-style rows, 24 render rows, 65
  parser rows, and 21 static-renderer out-of-scope rows.
- CSS implementation-depth gaps before the static-renderer completion target:
  49.
- Registry rows by support level: 43 required, 50 recommended, 16 experimental,
  21 out of scope.
- Registry rows by family:
  - aural-and-speech: 1
  - box-and-layout-core: 18
  - cascade-and-selection: 13
  - cssom-and-houdini-apis: 7
  - dynamic-timelines: 11
  - layout-systems: 11
  - lists-and-generated-content: 4
  - media-and-paged-output: 7
  - miscellaneous: 5
  - overflow-and-scrolling: 8
  - paint-and-visual-effects: 18
  - syntax-and-parsing: 4
  - text-and-fonts: 12
  - ui-and-forms: 4
  - values-and-units: 7

The current CSS module suite primarily proves inventory closure, parser
stability, and resource isolation. The next implementation phase must assign
each in-scope row a target oracle depth.

## Completion Scorecard

Every CSS module row should move through this scorecard. A row is not complete
until every applicable layer has active passing tests or a documented
non-applicable reason.

| Layer | Required evidence |
| --- | --- |
| Inventory | Registry row, W3C status, editor draft URL, support level, owner |
| Syntax | Tokenization, parse tree, error recovery, at-rule handling |
| Cascade | Origin, importance, specificity, layers, scope, inheritance, resets |
| Values | Specified, computed, used, actual values; shorthand expansion |
| Resources | `@import`, fonts, images, SVG, cursors, data URLs, blocked network |
| Layout | Formatting context, sizing, placement, fragmentation, overflow |
| Paint | Display list, stacking contexts, clipping, blending, raster output |
| Interop | WPT comparison or owned reduction for every high-risk behavior |
| Robustness | Stress, fuzz, timeout, memory, malformed inputs, security isolation |
| Documentation | Support table and known limitations are generated from coverage data |

## Target Engine Architecture

### CSS Front End

Implement a spec-shaped CSS front end that can preserve enough information for
debugging and later CSSOM work.

- CSS tokenizer with source spans and recovery mode.
- Parser for rules, declarations, at-rules, selectors, nested rules, functions,
  supports conditions, media queries, container queries, and unknown syntax.
- Stylesheet object model that preserves source order, layer order, namespace
  mappings, import boundaries, media/supports conditions, and invalid rules.
- Incremental import graph support with cycle detection and network-blocking
  policy.
- Deterministic serialization for test oracles.

Completion gates:

- All syntax-and-parsing rows have parser and negative recovery tests.
- Unknown at-rules and declarations are preserved or dropped according to spec
  without damaging following valid rules.
- Parser stress tests cover deeply nested rules, large selector lists, malformed
  functions, and long custom-property chains.

### Selector Matching

Build selector matching as an independent subsystem with explicit capability
flags.

- Type, class, id, attribute, namespace, combinators, pseudo-classes, and
  pseudo-elements.
- Level 4/5 selectors: `:is()`, `:where()`, `:not()` lists, `:has()`,
  `:nth-child()` of selector, scoping selectors, and shadow-related selectors
  where applicable.
- Specificity calculation as structured data, not ad hoc integers.
- Selector invalidation boundaries for future incremental style recalculation.

Completion gates:

- Selectors Level 3 and 4 renderer-relevant features have computed-style tests.
- Unsupported selectors fail locally and do not invalidate whole stylesheets
  unless the grammar requires it.
- `:has()` and complex list behavior have stress and performance tests.

### Cascade And Computed Style

Move all style resolution into a coherent cascade pipeline.

- Origins: user agent, user, author, inline style, presentational hints.
- Importance, specificity, source order, cascade layers, `@scope`, and nesting.
- Inheritance, initial, unset, revert, revert-layer, all.
- Custom properties, cycles, fallback substitution, invalid-at-computed-value
  time handling.
- Registered custom properties via `@property` for static behavior.
- Computed-style snapshots for every rendered element and pseudo-element.

Completion gates:

- All cascade-and-selection rows have computed-style tests.
- Shorthands expand deterministically and longhands serialize consistently.
- Custom property cycles and invalid substitutions match expected fallback
  behavior.
- Every visual test can optionally dump the computed-style path that caused a
  mismatch.

### Values And Units

Create a typed value layer shared by computed style, layout, and paint.

- Numeric types: lengths, percentages, angles, times, frequencies, resolutions,
  ratios, flex fractions, integers, numbers.
- Functions: `calc()`, `min()`, `max()`, `clamp()`, trigonometric and stepped
  functions where in scope.
- Colors: named colors, system colors, currentColor, rgb/hsl/hwb/lab/lch/oklab,
  color-mix, relative color syntax, wide gamut where raster backend permits.
- Environment variables and viewport/container-relative units where applicable.
- Serialization rules for specified, computed, and used values.

Completion gates:

- Values-and-units rows have computed-style and used-value tests.
- Unit conversion uses explicit reference contexts: viewport, font metrics,
  containing block, root font size, writing mode.
- Non-finite values and malformed function trees cannot escape into layout.

### Layout Core

Make layout a set of explicit formatting-context engines over a fragment tree.

- Block, inline, inline-block, floats, clearance, margin collapsing.
- Containing blocks, positioned layout, sticky/static/relative/absolute/fixed.
- Box sizing, min/max constraints, intrinsic sizes, aspect ratio.
- Fragment tree with enough detail for layout JSON and display-list tests.
- Writing modes and logical properties as first-class axes.
- Overflow, scroll boxes, clipping, scrollbar metrics where static rendering
  needs them.

Completion gates:

- CSS2.1, box model, display, positioning, sizing, overflow, and logical
  properties have layout and render tests.
- Every layout algorithm has targeted unit tests plus end-to-end render
  baselines.
- Layout outputs stable fragment IDs, geometry, baselines, and containing-block
  relationships for test diffs.

### Layout Systems

Implement modern layout systems as separate engines that share the typed value
and fragment infrastructure.

- Flexbox: line formation, flex base size, min-content constraints, alignment,
  wrapping, order, baseline alignment.
- Grid: track sizing, auto-placement, named lines, subgrid, gaps, alignment,
  intrinsic contributions.
- Tables: HTML table integration, column sizing, spans, captions, collapsed
  borders, section groups.
- Multi-column: column balancing, rules, spans, fragmentation strategy.
- Ruby, inline layout, regions/exclusions, line grid, round display, and page
  floats according to support policy.

Completion gates:

- Each layout system has algorithm-level tests, layout JSON tests, and render
  baselines.
- WPT-derived issue reductions are added for every discovered interoperability
  bug, without copying WPT fixtures into the repo.
- Layout performance is bounded for large grids, nested flex, and table spans.

### Text, Fonts, And Internationalization

Unify text shaping, metrics, bidi, line breaking, and font fallback.

- Font face loading, local fixtures, `@font-face`, weight/stretch/style
  matching, font-feature settings, variation axes where backend allows.
- Unicode bidi, directional isolation, vertical writing modes, ruby, text
  decoration, text transform, white-space, wrapping, hyphenation policy.
- Fallback fonts and deterministic metrics for CI.
- Inline painting: decorations, shadows, selection-like pseudo-elements if
  supported, emphasis marks, ruby annotations.

Completion gates:

- Text-and-fonts rows have computed-style, layout, and render tests.
- Bidi, CJK, Arabic, combining marks, ruby, vertical writing, and fallback font
  cases are deterministic.
- Font loading is resource-logged and cannot fetch remote URLs during tests.

### Painting And Effects

Build painting around a deterministic display list before rasterization.

- Background layers, borders, outlines, shadows, border radius, clipping.
- Stacking contexts, z-index, opacity, transforms, transform origins.
- Images, gradients, object fitting, image orientation where applicable.
- Filters, masks, clipping paths, shapes, blend modes, isolation.
- Color management policy, including wide-gamut fallback if backend cannot
  render full color spaces.

Completion gates:

- Paint-and-visual-effects rows have display-list and render tests.
- Every pixel baseline has a display-list counterpart for easier diagnosis.
- Unsupported effects produce deterministic fallback commands or documented
  negative behavior.

### Resources And Security

Treat resources as an engine subsystem, not only as parser side effects.

- Stylesheet imports, fonts, images, SVG, cursors, media metadata, object/embed,
  data URLs, local file URLs, and blocked remote URLs.
- URL resolution against document base URL and stylesheet base URL.
- Network-disabled default in tests and deterministic resource classification.
- Fixture-root isolation and protections against path traversal.
- Resource cache keying and cycle detection.

Completion gates:

- Every resource-bearing CSS feature has resource-log tests.
- Remote URLs are blocked by default and never silently fetched in CI.
- Missing, invalid, unsupported, and data URL resources have stable fallbacks.

## Module Family Roadmap

### Phase 1: Completion Infrastructure

- Add per-module implementation status files under `tests/html52/coverage/`.
- Extend coverage rows with `implementationStatus`, `oracleDepth`, `owner`,
  `blockedBy`, and `nextOracle`.
- Add dashboard sections for parser coverage, computed-style coverage, layout
  coverage, display-list coverage, render coverage, and API/runtime coverage.
- Add failing-on-purpose capability checks behind opt-in commands so future
  work can land incrementally without weakening release gates.

Exit criteria:

- Every in-scope CSS row has a target oracle depth.
- Every out-of-scope CSS row says whether it is out of scope forever or blocked
  by missing browser-runtime architecture.

Phase 1 artifacts:

- `tests/html52/coverage/css-modules.json` carries implementation metadata for
  every CSS current-work registry row.
- `tests/html52/coverage/css-module-implementation/*.json` groups per-module
  implementation status by CSS module family.
- `docs/compliance.md` reports implementation status, current oracle depth,
  target oracle depth, next oracle, owner, and scope-decision counts.
- `npm run html52:css-implementation:check` is an opt-in completion gate. It is
  expected to fail while in-scope modules have not reached their target oracle
  depth.

### Phase 2: CSS Front End And Cascade

Families:

- syntax-and-parsing
- cascade-and-selection
- values-and-units

Work:

- Replace parser sweep-only assertions with structured syntax and computed-style
  oracles.
- Implement or verify cascade layers, nesting, `@scope`, custom properties,
  registered properties, specificity, and selector functions.
- Add shorthand/longhand declaration matrices for every supported property.

Exit criteria:

- All required REC/CR/CRD syntax, selector, cascade, and value rows pass
  computed-style tests.
- Recommended WD rows have parser plus at least one computed-style or negative
  oracle.

Phase 2 artifacts:

- `npm run html52:css-generate-phase2-implementation` generates four HTML
  computed-style fixtures for cascade/selector behavior, values and shorthands,
  custom properties/style attributes, and registered custom properties.
- `npm run html52:css-computed` runs the focused `phase2-computed-style`
  subcluster.
- The merged suite now carries four computed-style CSS module cases, raising the
  root manifest to 147 active cases and reducing implementation-depth gaps from
  93 to 73.

### Phase 3: Core Layout

Families:

- box-and-layout-core
- overflow-and-scrolling
- ui-and-forms

Work:

- Complete block, inline, float, positioned, containing block, sizing,
  min/max, aspect-ratio, overflow, and logical property behavior.
- Add layout JSON oracles for all geometry-bearing cases.
- Expand form control static rendering and UA defaults.

Exit criteria:

- CSS2.1, Display 3, Box Model, Sizing, Position, Overflow, UI, and logical
  properties have layout and render baselines.
- No geometry-affecting CSS row remains parse-only unless explicitly
  experimental.

Phase 3 artifacts:

- `npm run html52:css-generate-phase3-implementation` generates three HTML
  layout fixtures for core layout, overflow/scrolling, and UI/form controls.
- `npm run html52:css-layout` runs the focused `phase3-layout-render`
  subcluster.
- The merged suite now carries three CSS module cases with both layout JSON and
  render PNG baselines, raising the root manifest to 150 active cases and
  reducing implementation-depth gaps from 73 to 49.

### Phase 4: Modern Layout Systems

Families:

- layout-systems
- media-and-paged-output
- lists-and-generated-content

Work:

- Implement Flexbox and Grid to production quality.
- Deepen table layout and collapsed border behavior.
- Implement multicolumn, fragmentation policy, generated content, counters, and
  list marker behavior.
- Decide print/paged-media product scope.

Exit criteria:

- Flexbox, Grid, Table, Lists, Counters, Multicol, Alignment, Fragmentation, and
  Generated Content have layout and render baselines.
- Large layout stress tests have bounded runtime and memory.

### Phase 5: Text, Fonts, And Writing Modes

Families:

- text-and-fonts
- aural-and-speech

Work:

- Complete font matching, `@font-face`, feature settings, fallback, text
  decoration, line breaking, bidi, ruby, vertical writing, and text transforms.
- Keep CSS Speech out of static visual rendering unless an aural backend is
  added.

Exit criteria:

- Text rows pass computed-style, layout, and render tests across deterministic
  fonts.
- Writing modes and logical layout interact correctly with layout systems.

### Phase 6: Paint, Images, And Effects

Families:

- paint-and-visual-effects
- miscellaneous paint-adjacent rows

Work:

- Complete backgrounds, borders, gradients, images, masks, filters, transforms,
  shapes, compositing, blending, color adjustment, and wide-gamut color policy.
- Add display-list oracles for every paint feature.
- Add render baselines only after display-list stability is established.

Exit criteria:

- Paint rows have display-list and render tests.
- Visual mismatches can be diagnosed without looking only at final pixels.

### Phase 7: Runtime CSS Platform Decision

Families:

- dynamic-timelines
- cssom-and-houdini-apis

Work if the product remains a static renderer:

- Keep these rows out of release gates.
- Add negative/no-crash parser coverage where stylesheet syntax is relevant.
- Document unsupported runtime APIs in generated support tables.

Work if the product targets full CSS platform 100%:

- Add a live DOM and CSSOM mutation model.
- Add event loop and animation timelines.
- Add viewport, scrolling, and geometry APIs.
- Add Font Loading API promises and lifecycle.
- Add Typed OM objects and serialization.
- Add Houdini worklet host for Paint/Layout/Animation APIs.
- Add Resize Observer and Custom Highlight API lifecycles.

Exit criteria for static renderer:

- Runtime rows remain classified and tested for safe parsing or deterministic
  non-support.

Exit criteria for full platform:

- Runtime API WPT-aligned behavior passes and can be observed from script-level
  tests. This requires a separate JS/runtime-capable suite.

## Test Strategy To Prove Completion

### Owned Tests

- Keep all repository-owned HTML/CSS tests in `tests/html52`.
- Add generated matrices for property values, selectors, media/container
  queries, custom properties, shorthands, layout constraints, and paint effects.
- Keep deterministic JSON references for parser, computed style, layout,
  display list, and resource log.
- Keep deterministic PNG baselines for final renderer behavior.

### WPT Relationship

- Use WPT and `wpt.fyi` for discovery, prioritization, and interop comparison.
- Do not copy WPT fixtures or references into this repository.
- When WPT exposes a bug, add a small Broiler-owned reduction with a spec link
  and local baseline.

### Differential And Fuzz Testing

- Build CSS fuzzers that generate valid and invalid declaration blocks,
  selectors, nested rules, custom property graphs, and layout trees.
- Compare parse and computed-style behavior against a selected browser only as
  an advisory signal, then commit owned expected outputs.
- Run crash, timeout, and allocation guards in CI for generated stress inputs.

## Release Gates

Required gates for static renderer 100%:

- `npm run html52:integrated:check`
- `npm run html52:comprehensive`
- `npm run html52:repeat`
- `npm run html52:coverage`
- `npm run html52:dashboard:check`
- `npm run html52:css-registry:check`
- `npm run html52:css-coverage:check`
- all implementation-status rows at or above their target oracle depth
- no unclassified CSS current-work rows
- no parse-only status for required geometry or paint features
- no unbounded-time or unbounded-memory stress case

Additional gates for full CSS platform 100%:

- runtime-capable JS/API suite
- animation timeline determinism suite
- CSSOM and Typed OM mutation/query suite
- viewport/scrolling geometry suite
- Houdini worklet suite or explicit product decision to not claim platform 100%

## Prioritized Implementation Backlog

1. Add implementation-depth tracking to CSS coverage.
2. Implement complete computed-style dumping and property metadata.
3. Convert required syntax/cascade/value rows from parser-only to computed-style
   oracles.
4. Finish core layout JSON oracles for block, inline, float, positioning,
   sizing, overflow, and logical properties.
5. Deepen Flexbox and Grid with layout-system-specific algorithm tests.
6. Deepen text/font/writing-mode behavior with deterministic font fixtures.
7. Introduce display-list oracles for paint features before adding more PNG
   baselines.
8. Add resource graph tests for CSS imports, fonts, images, cursors, and SVG
   across stylesheet base URLs.
9. Add WPT-driven issue reductions for each high-risk module family.
10. Decide whether browser-runtime CSS APIs are product scope. If yes, plan a
    separate runtime architecture; if no, keep them classified as out of scope.

## Risks

- Calling the current parser/no-crash CSS module coverage "100%" would overstate
  conformance.
- Full CSS platform 100% is impossible without a browser runtime architecture.
- Pixel-only tests make failures expensive to diagnose; display-list and layout
  JSON oracles must land first.
- Advanced text and font behavior can be nondeterministic without pinned fonts
  and a stable shaping backend.
- Draft CSS modules can change faster than implementation work; release gates
  should distinguish required, recommended, and experimental support levels.

## Immediate Next Steps

1. Add `implementationStatus` and `oracleDepth` fields to CSS coverage rows.
2. Generate a report of CSS rows that are currently parser-only but should
   become computed-style, layout, display-list, or render tests.
3. Start with cascade, selectors, custom properties, values, and shorthands,
   because they feed every later layout and paint feature.
4. Add layout JSON baselines for existing visual CSS tests.
5. Create a support table generated from coverage data that distinguishes
   "implemented", "parse only", "negative/no-crash", "experimental", and
   "out of scope".
