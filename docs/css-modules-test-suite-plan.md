# CSS Modules Test Suite Plan

Date: 2026-06-21
Target repository: Broiler.HTML
Primary standards snapshot: CSS Snapshot 2026, W3C Group Note, 26 March 2026
Live registry source: W3C CSS current work page, checked on 2026-06-21

## Purpose

This document plans a repository-owned CSS modules test suite integrated with
the existing `tests/html52` runner model. The suite should cover the current
versions and module levels tracked by the CSS Working Group, while staying
honest about the difference between a static HTML renderer and a full browser
engine.

"All current versions of all CSS modules" means every non-obsolete CSS Working
Group current-work entry whose current status is REC, CRD, CR, WD, or FPWD.
As of 2026-06-21, that registry contains 130 active entries: 12 REC, 15 CRD,
19 CR, 65 WD, and 19 FPWD. Notes, superseded specs, historical CSS snapshots,
and abandoned specs are tracked separately as compatibility or out-of-scope
items.

CSS itself does not have a single traditional version. It is defined by module
levels, with newer levels refining or extending earlier ones. The suite must
therefore track module level, current W3C status, upstream URL, renderer
relevance, and test coverage independently.

## Standards Sources

- CSS Snapshot 2026: https://www.w3.org/TR/css-2026/
- CSS current work: https://www.w3.org/Style/CSS/current-work.en.html
- CSS Working Group editor drafts: https://drafts.csswg.org/
- W3C CSS standards/drafts index: https://www.w3.org/TR/?tags%5B0%5D=css
- External implementation reports and comparison data: https://wpt.fyi/

The checked-in suite should not copy WPT fixtures or reference files into this
repository. WPT and `wpt.fyi` are useful for prioritization, implementation
report comparison, and discovering interoperability risk, but Broiler-owned
cases and baselines remain the source of truth.

## Fit With The HTML 5.2 Suite

The existing HTML 5.2 suite already has the right shape: explicit manifest
entries, local assets, deterministic rendering, checked-in references, coverage
maps, and status accounting. The CSS modules suite should extend that shape
instead of creating a parallel runner.

Proposed additions:

```text
tests/html52/
  coverage/
    css-modules.json
    css-module-sections/
      css-syntax-3.json
      css-cascade-5.json
      ...
  cases/
    css-modules/
      css-syntax-3/
      css-cascade-5/
      css-values-3/
      css-display-3/
      css-grid-2/
      ...
  references/
    computed-style/
    layout/
    display-list/
    images/
    resource-log/
  generated/
    css-modules/
      registry.json
      manifest.generated.json
      section-index.json
scripts/html52/
  generate-css-module-registry.mjs
  generate-css-module-coverage.mjs
```

The generated registry records W3C status and URLs. The hand-authored coverage
files classify renderer relevance and support policy. Generated manifest files
should be loaded through the existing `generatedManifests` field once the
validator and runner are taught to merge them.

## Manifest Shape

CSS module cases can use the current manifest schema with conservative naming:

```json
{
  "id": "css-cascade-5-layer-order-001",
  "title": "Cascade layers sort before specificity within an origin",
  "cluster": "css",
  "subcluster": "css-cascade-5",
  "featureId": "css-module-css-cascade-5-layers",
  "spec": [
    {
      "section": "6.4",
      "url": "https://www.w3.org/TR/css-cascade-5/#cascade-layers"
    }
  ],
  "input": "cases/css-modules/css-cascade-5/layer-order-001.html",
  "assertions": [
    "Rules in later cascade layers override earlier layers at the same origin."
  ],
  "expectations": {
    "computedStyle": "references/computed-style/css-cascade-5-layer-order-001.json",
    "render": "references/images/css-cascade-5-layer-order-001.png"
  },
  "requires": ["css-parser", "css-cascade", "computed-style", "render"],
  "scripts": "not-required",
  "status": "planned"
}
```

Runner work needed before the full suite can execute:

- merge generated manifests with the root manifest before validation
- implement `computedStyle`, `layout`, and `displayList` expectation checks
- allow CSS-module filters such as `--css-module css-cascade-5`
- report uncovered CSS module rows separately from HTML 5.2 rows
- support expected-unsupported and parse-only expectations without marking a
  renderer-inapplicable module as failed

## Coverage Definition

Every registry row must have one coverage row in `css-modules.json`. Every
normative subsection relevant to static rendering must then have a lower-level
coverage item in `coverage/css-module-sections/*.json`.

Coverage states:

- `active`: executable cases exist and are expected to pass
- `planned`: in scope, no executable case yet
- `blocked`: in scope, but a missing harness capability prevents useful tests
- `optional`: useful but not required for Broiler's renderer envelope
- `out-of-scope`: browser API, live timeline, accessibility platform API, or
  other behavior outside a static renderer
- `negative`: unsupported feature is intentionally recognized, ignored, or
  rejected without crashing

Support levels:

- `required`: REC/CR/CRD feature that affects static parsing, cascade, layout,
  resource loading, text, or painting
- `recommended`: WD feature with broad implementation or direct relevance to
  modern authored content
- `experimental`: FPWD or volatile WD feature, inventoried and smoke-tested
  without making pixel parity a release gate
- `out-of-scope`: not a static renderer responsibility, but still represented
  in the dashboard

## Oracle Types

Pixels are necessary but not enough. CSS module tests should combine oracle
types so failures point to the right layer.

- `tokens`: CSS tokenizer/parser output for syntax recovery and at-rules
- `computedStyle`: selected computed values for cascade, inheritance, custom
  properties, media queries, selectors, values, and shorthands
- `layout`: fragment tree, containing blocks, intrinsic sizes, baselines,
  scroll boxes, and placement
- `displayList`: paint order, clipping, stacking contexts, filters, masks, and
  blend mode commands before rasterization
- `render`: deterministic PNG for end-to-end visual output
- `resourceLog`: stylesheet, font, image, cursor, SVG, data URL, and blocked
  network behavior
- `negative`: unsupported, ignored, or inapplicable features that must not
  crash or corrupt later declarations

## Coverage Policy By Status

REC:
Full static-renderer coverage is required. Every normative section that affects
parsing, cascade, computed style, layout, text, resources, or painting must map
to executable tests or a documented out-of-scope row.

CR and CRD:
Full coverage is required for renderer-relevant features. Features that are
API-only, timeline-only, speech-only, or platform-only get negative or
out-of-scope coverage so the dashboard shows they were considered.

WD:
Coverage is recommended when the feature is already common in authored content,
listed in CSS Snapshot safe-to-release exceptions, or implemented in Broiler.
Otherwise, WD modules need a section inventory, parser/no-crash smoke tests,
and explicit support classification.

FPWD:
Coverage is experimental. Keep an inventory row, parse/no-crash smoke cases,
and a feature flag or quarantine policy. Do not make exact rendering a release
gate until the module has implementation experience or Broiler intentionally
targets it early.

NOTE, SPSD, historical snapshots, and abandoned specs:
Track only when they affect legacy content or compatibility. CSS Level 2
Revision 1 remains required because CSS Snapshot 2026 includes CSS2 as the
stable core that later modules override.

## Module Families

The registry should be grouped into suite families so implementation and triage
stay navigable.

| Family | Modules | Primary oracle types |
| --- | --- | --- |
| CSS syntax and parsing | CSS Syntax, CSS Nesting, CSS Mixins, Style Attributes | `tokens`, `computedStyle`, `negative` |
| Cascade and selection | Selectors, Cascade, Conditional Rules, Namespaces, Scoping, Custom Properties | `computedStyle`, `render` |
| Values and units | Values and Units, Environment Variables, Easing, Geometry Interfaces | `computedStyle`, `negative` |
| Box and layout core | CSS2, Box, Display, Sizing, Position, Anchor Positioning, Containment, Logical Properties | `layout`, `render` |
| Layout systems | Flexbox, Grid, Box Alignment, Multi-column, Tables, Exclusions, Regions, Template Layout | `layout`, `render` |
| Overflow and scrolling | Overflow, Scroll Snap, Scrollbars, Scroll Anchoring, Overscroll Behavior | `layout`, `render`, `negative` |
| Text and fonts | Fonts, Font Loading, Text, Text Decoration, Inline, Ruby, Writing Modes, Line Grid, Rhythmic Sizing | `computedStyle`, `layout`, `render` |
| Lists and generated content | Lists, Counter Styles, Generated Content, Generated Content for Paged Media | `computedStyle`, `layout`, `render` |
| Paint and visual effects | Color, Color Adjustment, Backgrounds/Borders, Images, Masking, Transforms, Fill/Stroke, Filters, Compositing | `displayList`, `render`, `resourceLog` |
| UI and forms | Basic UI, Forms, Viewport, Round Display, Spatial Navigation | `computedStyle`, `render`, `negative` |
| Dynamic timelines | Animations, Transitions, Web Animations, Scroll-driven Animations, View Transitions, Animation Worklet | `computedStyle`, `negative` |
| CSSOM and Houdini APIs | CSSOM, CSSOM View, Typed OM, Paint API, Layout API, Properties and Values API, Worklets, Resize Observer, Highlight API | `negative`, `resourceLog` |
| Media and paged output | Media Queries, Paged Media, Fragmentation, Page Floats, Print Profile compatibility | `computedStyle`, `layout`, `render` |
| Aural and speech | CSS Speech | `negative` unless an aural backend is added |

## Rollout Plan

Phase 0: Registry and dashboard

- Add `scripts/html52/generate-css-module-registry.mjs`.
- Fetch or vendor a normalized snapshot of the W3C current-work table.
- Generate `tests/html52/generated/css-modules/registry.json`.
- Add `tests/html52/coverage/css-modules.json` with one row per active
  registry entry.
- Extend coverage summaries to include CSS module counts and uncovered rows.

Phase 1: Harness gaps

- Add `computedStyle`, `layout`, and `displayList` expectation execution.
- Add CSS parser/token dump support if it is not already available through
  `Broiler.HTML.Tool`.
- Add per-case module filters and generated-manifest merging.
- Add a CSS-only smoke command, for example `npm run html52:css`.

Phase 2: Stable core modules

- Cover CSS2, CSS Syntax 3, CSS Cascade 3/4/5, Selectors 3/4, Values 3,
  Style Attributes, Namespaces, Custom Properties 1, Media Queries 3/4,
  Conditional Rules 3/4, Color 3/4, Fonts 3, Text 3, Writing Modes 3/4,
  Display 3, Box 3, Sizing 3, Backgrounds and Borders 3, Images 3,
  Transforms 1, Flexbox 1, Grid 1/2, Tables 3, and CSS UI 3.
- Prefer small, focused cases with computed-style or layout assertions before
  adding final render baselines.

Phase 3: Renderer breadth

- Add multi-column, fragmentation, scrollbars, overflow, scroll snap, masks,
  compositing, filters, text decoration, shapes, counters, lists, generated
  content, ruby, inline layout, logical properties, containment, and color
  adjustment.
- Add cross-module cases for interactions such as logical properties plus
  writing modes, grid plus alignment, overflow plus positioning, text plus
  decoration, and background clipping plus border radius.

Phase 4: Draft and early-draft modules

- For WD modules with broad implementation, add recommended coverage once
  Broiler has corresponding feature support or intentional non-support rows.
- For FPWD modules, add parser/no-crash cases and dashboard rows first.
- Keep volatile rendering assertions quarantined until their status or browser
  interoperability improves.

Phase 5: Stress, fuzz, and regression generation

- Generate declaration matrices for shorthands/longhands, invalid-at-computed-
  value-time cases, cascading origins, inheritance, custom property cycles,
  selector specificity, media-query permutations, layout min/max constraints,
  and paint-order combinations.
- Add resource isolation tests for `@import`, fonts, images, cursors, data URLs,
  and blocked network access.
- Add crash/time/memory tests for deeply nested rules, large selector lists,
  long custom-property chains, malformed `calc()` trees, repeated gradients,
  and extreme layout constraints.

## Minimum Exit Criteria

The CSS modules plan is complete enough to call the suite "covered" only when:

- all 130 active registry rows have coverage records
- every REC/CR/CRD renderer-relevant normative section has at least one
  executable case or an explicit blocker
- all WD/FPWD modules have support classification and at least parser/no-crash
  coverage where syntax can appear in a stylesheet
- `npm run html52:coverage` fails when required CSS module rows are uncovered
- `npm run html52:css` validates manifests and executes the CSS smoke subset
- full scheduled CI runs stable CSS module cases and publishes render, layout,
  computed-style, display-list, and coverage artifacts
- every out-of-scope row names the precise reason and the missing runtime
  capability, not just "unsupported"

## Appendix A: Registry Seed

This table is a seed generated from the W3C CSS current-work table on
2026-06-21. It should be regenerated before implementation work begins and
whenever CSSWG statuses change.

| Status | Count |
| --- | ---: |
| REC | 12 |
| CRD | 15 |
| CR | 19 |
| WD | 65 |
| FPWD | 19 |
| Total active rows | 130 |

| Status | Module id | Module |
| --- | --- | --- |
| REC | color | CSS Color Level 3 |
| REC | namespace | CSS Namespaces |
| REC | selectors | Selectors Level 3 |
| REC | css21 | CSS Level 2 Revision 1 |
| REC | mediaqueries | Media Queries Level 3 |
| REC | style-attr | CSS Style Attributes |
| REC | cascade | CSS Cascading and Inheritance Level 3 |
| REC | fonts | CSS Fonts Level 3 |
| REC | writing-modes | CSS Writing Modes Level 3 |
| REC | ui | CSS Basic User Interface Level 3 |
| REC | box | CSS Box Model Level 3 |
| REC | css-contain-1 | CSS Containment Level 1 |
| CRD | background | CSS Backgrounds and Borders Level 3 |
| CRD | speech | CSS Speech Level 1 |
| CRD | text-decor | CSS Text Decoration Level 3 |
| CRD | shapes | CSS Shapes Level 1 |
| CRD | css-masking | CSS Masking Level 1 |
| CRD | text | CSS Text Level 3 |
| CRD | syntax | CSS Syntax Level 3 |
| CRD | grid-layout | CSS Grid Layout Level 1 |
| CRD | css-will-change-1 | CSS Will Change Level 1 |
| CRD | mediaqueries-4 | Media Queries Level 4 |
| CRD | css-paint-api-1 | CSS Painting API Level 1 |
| CRD | css-color-4 | CSS Color Level 4 |
| CRD | css-timing-1 | CSS Easing Functions Level 1 |
| CRD | css-grid-2 | CSS Grid Layout Level 2 |
| CRD | css-color-adjust-1 | CSS Color Adjustment Level 1 |
| CR | conditional | CSS Conditional Rules Level 3 |
| CR | multicol | CSS Multi-column Layout Level 1 |
| CR | values | CSS Values and Units Level 3 |
| CR | flexbox | CSS Flexible Box Layout Level 1 |
| CR | counter-styles | CSS Counter Styles Level 3 |
| CR | images | CSS Images Level 3 |
| CR | break | CSS Fragmentation Level 3 |
| CR | transforms | CSS Transforms Level 1 |
| CR | variables | CSS Custom Properties for Cascading Variables Level 1 |
| CR | compositing | Compositing and Blending Level 1 |
| CR | css-display-3 | CSS Display Level 3 |
| CR | geometry-1 | Geometry Interfaces Level 1 |
| CR | cascade-4 | CSS Cascading and Inheritance Level 4 |
| CR | css-snappoints-1 | CSS Scroll Snap Level 1 |
| CR | writing-modes-4 | CSS Writing Modes Level 4 |
| CR | css-scrollbars-1 | CSS Scrollbars Styling Level 1 |
| CR | css-conditional-4 | CSS Conditional Rules Level 4 |
| CR | css-cascade-5 | CSS Cascading and Inheritance Level 5 |
| CR | css-view-transitions-1 | CSS View Transitions Level 1 |
| WD | animations | CSS Animations Level 1 |
| WD | web-animations | Web Animations |
| WD | transitions | CSS Transitions |
| WD | align | CSS Box Alignment Level 3 |
| WD | selectors4 | Selectors Level 4 |
| WD | sizing | CSS Box Sizing Level 3 |
| WD | lists | CSS Lists and Counters Level 3 |
| WD | positioning | CSS Positioned Layout Level 3 |
| WD | motion-1 | Motion Path Level 1 |
| WD | css-fonts-4 | CSS Fonts Level 4 |
| WD | css-logical-1 | CSS Logical Properties and Values Level 1 |
| WD | css-values-4 | CSS Values and Units Level 4 |
| WD | css-contain-2 | CSS Containment Level 2 |
| WD | paged-media | CSS Paged Media Level 3 |
| WD | cssom-view | CSSOM View |
| WD | ruby | CSS Ruby Annotation Layout Level 1 |
| WD | cssom | CSS Object Model (CSSOM) |
| WD | css-overflow-3 | CSS Overflow Level 3 |
| WD | css-font-loading-3 | CSS Font Loading Level 3 |
| WD | pseudo-4 | CSS Pseudo-Elements Level 4 |
| WD | css-images-4 | CSS Image Values and Replaced Content Level 4 |
| WD | css-overflow-4 | CSS Overflow Level 4 |
| WD | css-text-decor-4 | CSS Text Decoration Level 4 |
| WD | mediaqueries-5 | Media Queries Level 5 |
| WD | css-sizing-4 | CSS Box Sizing Level 4 |
| WD | device-adapt | CSS Viewport Level 1 |
| WD | exclusions | CSS Exclusions |
| WD | filter | Filter Effects Level 1 |
| WD | gcpm | CSS Generated Content for Paged Media |
| WD | linegrid | CSS Line Grid |
| WD | regions | CSS Regions |
| WD | tables | CSS Table Level 3 |
| WD | inline | CSS Inline Layout Level 3 |
| WD | css-round-display-1 | CSS Round Display Level 1 |
| WD | css-ui-4 | CSS Basic User Interface Level 4 |
| WD | css-text-4 | CSS Text Level 4 |
| WD | css-properties-values-api-1 | CSS Properties and Values API Level 1 |
| WD | css-typed-om-1 | CSS Typed OM Level 1 |
| WD | css-rhythm-1 | CSS Rhythmic Sizing Level 1 |
| WD | css-shadow-parts-1 | CSS Shadow Parts |
| WD | css-nav-1 | CSS Spatial Navigation Level 1 |
| WD | css-scroll-anchoring-1 | CSS Scroll Anchoring Level 1 |
| WD | css-color-5 | CSS Color Level 5 |
| WD | css-transforms-2 | CSS Transforms Level 2 |
| WD | css-box-4 | CSS Box Model Level 4 |
| WD | css-highlight-api-1 | CSS Custom Highlight API Level 1 |
| WD | css-fonts-5 | CSS Fonts Level 5 |
| WD | css-nesting-1 | CSS Nesting |
| WD | css-cascade-6 | CSS Cascading and Inheritance Level 6 |
| WD | css-conditional-5 | CSS Conditional Rules Level 5 |
| WD | css-contain-3 | CSS Containment Level 3 |
| WD | scroll-animations-1 | Scroll-driven Animations |
| WD | css-animations-2 | CSS Animations Level 2 |
| WD | web-animations-2 | Web Animations Level 2 |
| WD | css-transitions-2 | CSS Transitions Level 2 |
| WD | css-anchor-position-1 | CSS Anchor Positioning |
| WD | css-view-transitions-2 | CSS View Transitions Level 2 |
| WD | css-values-5 | CSS Values and Units Level 5 |
| WD | css-grid-3 | CSS Grid Layout Level 3 |
| WD | css-color-hdr-1 | CSS Color HDR Level 1 |
| WD | css-display-4 | CSS Display Level 4 |
| WD | css-gaps-1 | CSS Gap Decorations Level 1 |
| WD | css-position-4 | CSS Positioned Layout Level 4 |
| WD | css-borders-4 | CSS Borders and Box Decorations Level 4 |
| WD | content | CSS Generated Content Level 3 |
| FPWD | css-scoping-1 | CSS Scoping Level 1 |
| FPWD | resize-observer-1 | Resize Observer |
| FPWD | css4-background | CSS Backgrounds Level 4 |
| FPWD | page-floats | CSS Page Floats |
| FPWD | fill-stroke-3 | CSS Fill and Stroke Level 3 |
| FPWD | css-layout-api-1 | CSS Layout API Level 1 |
| FPWD | css-break-4 | CSS Fragmentation Level 4 |
| FPWD | css-overscroll-1 | CSS Overscroll Behavior Level 1 |
| FPWD | css-animation-worklet-1 | CSS Animation Worklet API |
| FPWD | css-scroll-snap-2 | CSS Scroll Snap Level 2 |
| FPWD | css-easing-2 | CSS Easing Functions Level 2 |
| FPWD | css-overflow-5 | CSS Overflow Level 5 |
| FPWD | css-multicol-2 | CSS Multi-column Layout Level 2 |
| FPWD | css-mixins-1 | CSS Functions and Mixins |
| FPWD | css-env-1 | CSS Environment Variables Level 1 |
| FPWD | css-anchor-position-2 | CSS Anchor Positioning Level 2 |
| FPWD | selectors-5 | Selectors Level 5 |
| FPWD | css-image-animation-1 | CSS Image Animation Level 1 |
| FPWD | css22 | Preview of CSS Level 2 |

## Appendix B: First Coverage Rows To Add

These rows are the first stable set to create in `css-modules.json` before
section-level expansion:

- `css-module-css21`: CSS Level 2 Revision 1 core cascade, visual formatting,
  box model, tables, generated content, colors, fonts, and media types.
- `css-module-syntax`: CSS Syntax Level 3 parsing, tokenization, escapes,
  error recovery, comments, at-rules, blocks, and declaration lists.
- `css-module-cascade`: Cascade Levels 3, 4, and 5, including origins,
  importance, inheritance, `all`, `revert`, `revert-layer`, and layers.
- `css-module-selectors`: Selectors Levels 3 and 4, specificity, selector
  lists, pseudo-classes, pseudo-elements, invalid selector handling, and
  matching against HTML parser output.
- `css-module-values`: Values and Units Levels 3 and 4, including numeric
  grammar, lengths, percentages, angles, times, resolution, `calc()`, min/max,
  clamp, URLs, strings, identifiers, and invalid-at-computed-value time.
- `css-module-display-layout`: Display 3, Box 3, Sizing 3, Position 3,
  Containment 1/2, Logical 1, Flexbox 1, Grid 1/2, Alignment 3, Multi-column 1,
  and Table 3.
- `css-module-text-fonts`: Fonts 3/4, Font Loading 3, Text 3/4, Text
  Decoration 3/4, Inline 3, Ruby 1, Writing Modes 3/4, and Counter Styles 3.
- `css-module-paint`: Color 3/4/5, Color Adjustment 1, Backgrounds and Borders
  3/4, Images 3/4, Masking 1, Transforms 1/2, Filters 1, Compositing 1, and
  Fill and Stroke 3.
- `css-module-dynamic-api-boundary`: Animations, Transitions, Web Animations,
  View Transitions, CSSOM, Typed OM, Paint API, Layout API, Properties and
  Values API, Worklets, Resize Observer, Highlight API, Speech, and other
  browser-runtime-dependent modules.
