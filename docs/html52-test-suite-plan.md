# HTML 5.2 Plain-HTML Test Suite Plan

Date: 2026-06-20
Target repository: Broiler.HTML
Primary standard snapshot: W3C HTML 5.2 Recommendation, 14 December 2017

## Purpose

This document plans a complete, repository-owned HTML 5.2 test suite for a
static HTML renderer. The suite must not import Web Platform Tests fixtures and
must not depend on JavaScript execution. It should test plain documents,
static resources, parser behavior, layout, painting, resource loading, and
renderer robustness in a way that can run locally and in CI.

"Complete" here means every HTML 5.2 renderer-relevant feature is represented
in an explicit coverage matrix as one of:

- covered by executable tests
- covered by generated tests
- covered by static visual baselines
- intentionally out of scope for a static renderer
- blocked by a missing harness capability

This avoids pretending that a browser API suite and a document renderer suite
are the same thing. HTML 5.2 includes APIs, navigation, drag and drop, media
playback, scripting, and form submission. A plain renderer still needs to parse
the related markup, expose or record semantics where useful, and render static
states, but it does not need to execute scripts or behave as a full browser.

## Non-negotiable constraints

- No WPT test files, reference files, metadata, or exclusion manifests are used
  as source material.
- No test depends on JavaScript execution.
- Test pages may contain `script` markup only when the assertion is about HTML
  parsing, raw-text handling, fallback, inert behavior, or default rendering.
- Browser screenshots may be used only as an optional bootstrap aid for
  baselines; checked-in expectations must be owned by this repository.
- Network access must be disabled by default during suite execution. All
  resources come from the fixture tree or generated data URLs.
- Tests must be deterministic across Windows, Linux, and macOS as far as the
  renderer backend allows.
- Every case must have machine-readable metadata that maps it back to a spec
  section, feature, expectation type, and support status.

## References

- W3C HTML 5.2 Recommendation: https://www.w3.org/TR/2017/REC-html52-20171214/
- HTML 5.2 top-level areas used for coverage: common infrastructure, document
  semantics, elements, user interaction, loading, web application APIs, HTML
  syntax, XML syntax, rendering, and obsolete features.
- Existing repository harness pieces: `Broiler.HTML.Tool`,
  `PixelDiffRunner`, `MismatchClassifier`, deterministic IR JSON dumpers, and
  image/font loading hooks.

## Renderer competition envelope

The test suite should reflect the real kinds of content an HTML renderer must
handle:

- standards-shaped documents authored for modern browsers
- old or malformed web pages that rely on HTML error recovery
- static application snapshots with heavy CSS and form controls
- documentation pages with tables, code blocks, lists, headings, anchors, and
  syntax-highlighted spans
- international text with bidirectional, CJK, Arabic, combining, ruby, and
  fallback font behavior
- resource-rich documents with images, SVG, external CSS, data URLs, broken
  resources, and relative URL resolution
- print/export style documents where pixel stability, pagination-like sizing,
  and table fidelity matter
- hostile or accidental inputs that must not crash, hang, allocate
  unboundedly, or read outside the fixture root

## Scope boundaries

### In scope

- HTML byte/string input handling and document setup
- tokenization, character references, comments, doctypes, raw text, and
  malformed syntax recovery
- tree construction behavior that affects rendering, including implicit
  elements, omitted end tags, tables, forms, templates, and foreign content
- element-specific semantics and default rendering for all HTML 5.2 elements
  that can appear in static documents
- static states of form controls and interactive elements
- CSS needed to render HTML documents competitively, including user-agent
  defaults, cascade, selectors, layout, typography, tables, lists, replaced
  elements, painting, and resource-linked stylesheets
- XHTML/XML syntax parsing and rendering where supported
- obsolete and legacy markup where static rendering behavior is expected by
  real content
- crash, timeout, determinism, and resource-isolation tests

### Out of scope for this plain-renderer suite

- JavaScript execution semantics
- DOM mutation after parse
- timers, workers, storage, history, navigation, browsing-context security, and
  cross-origin window behavior
- live media playback, seeking, timed track APIs, and media event sequencing
- drag-and-drop event processing
- form submission over HTTP
- editing command APIs
- canvas drawing APIs, except fallback rendering and static element sizing
- accessibility platform API integration, except for optional semantic dumps

Each out-of-scope item still needs at least one "negative coverage" entry so
the coverage dashboard records that it was considered intentionally.

## Suite architecture

### Proposed tree

```text
tests/
  html52/
    README.md
    manifest.schema.json
    manifest.json
    coverage/
      spec-map.json
      elements.json
      attributes.json
      css-requirements.json
      out-of-scope.json
    assets/
      css/
      fonts/
      images/
      svg/
      xml/
    cases/
      syntax/
      tree-construction/
      document-metadata/
      elements/
      forms/
      tables/
      embedded-content/
      links/
      user-interaction/
      css/
      layout/
      text-i18n/
      painting/
      resources/
      xhtml/
      obsolete/
      stress/
    references/
      images/
      dom/
      layout/
      display-list/
      resource-log/
    generated/
      cases/
      references/
      manifest.generated.json
scripts/
  html52/
    run-suite.mjs
    generate-suite.mjs
    validate-manifest.mjs
    update-baselines.mjs
    summarize-coverage.mjs
```

`generated/` should be reproducible from checked-in metadata and generator
code. Hand-authored tests live under `cases/`.

### Test case shape

Every executable case has a manifest entry, not implicit discovery alone.

```json
{
  "id": "html52-tree-p-implied-end-001",
  "title": "A block start tag implicitly closes an open p element",
  "cluster": "tree-construction",
  "subcluster": "implied-end-tags",
  "spec": [
    {
      "section": "8.2.5.4.7",
      "url": "https://www.w3.org/TR/2017/REC-html52-20171214/syntax.html#parsing-main-inbody"
    }
  ],
  "input": "cases/tree-construction/p-implied-end-001.html",
  "assertions": [
    "The div is a sibling of the p, not a child of the p."
  ],
  "expectations": {
    "dom": "references/dom/tree-p-implied-end-001.json",
    "render": "references/images/tree-p-implied-end-001.png"
  },
  "viewports": [
    { "width": 800, "height": 600, "deviceScaleFactor": 1 }
  ],
  "fonts": ["DeterministicSans"],
  "tolerance": {
    "pixelDiffThreshold": 0.001,
    "colorTolerance": 2
  },
  "requires": ["html-parser", "css", "layout"],
  "scripts": "not-required",
  "status": "planned"
}
```

### Expectation types

Use multiple oracle types because visual pixels alone cannot explain parser and
layout defects.

- `tokens`: expected token sequence for tokenizer-only cases
- `dom`: expected normalized tree, including implicit nodes and attributes
- `computed-style`: expected selected computed values for targeted nodes
- `layout`: expected fragment tree with rounded, deterministic coordinates
- `display-list`: expected paint commands before rasterization
- `render`: expected PNG output
- `resource-log`: expected local resource requests, resolved URLs, MIME guesses,
  and failures
- `semantic-outline`: optional expected headings, landmarks, form associations,
  and ARIA-related static metadata
- `negative`: expected parse error, unsupported feature classification, timeout
  guard, or "no crash" result

### Runner model

The new runner should be independent from the existing WPT runner but reuse
repository primitives where possible.

1. Validate `manifest.json` and generated manifests against
   `manifest.schema.json`.
2. Build `Broiler.HTML.Tool` once.
3. Start a fixture-local static server only when HTTP URL behavior is under
   test; otherwise render from files using file URLs.
4. Register deterministic fixture fonts.
5. Render each case with fixed viewport, color scheme, device scale, and
   resource root.
6. Capture requested dumps from the renderer.
7. Compare JSON expectations with stable ordering and numeric tolerances.
8. Compare images with `PixelDiffRunner` and classify failures with
   `MismatchClassifier`.
9. Write per-case artifacts and a suite summary.
10. Emit CI-friendly Markdown and optional JUnit XML.

Initial command sketch:

```bash
npm run html52:run -- --include tree-construction --include rendering --jobs 4
npm run html52:coverage
npm run html52:update-baselines -- --case html52-rendering-hidden-001
```

### Required renderer/tooling additions

The current CLI can render and compare images. A complete suite also needs
inspection commands:

- `dump-tokens --input file.html --output tokens.json`
- `dump-dom --input file.html --output dom.json`
- `dump-computed-style --input file.html --selector "#target"`
- `dump-layout --input file.html --output fragments.json`
- `dump-display-list --input file.html --output display-list.json`
- `render --resource-log resource-log.json`
- `render --disable-network` or equivalent resource sandboxing
- `render --viewport-width --viewport-height --device-scale-factor`
- `render --deterministic-mode` to freeze clocks, system colors, fonts, and
  image error placeholders

These commands can be added incrementally. Early phases can rely on visual
tests plus the existing fragment/display-list JSON dumpers if they can be
exposed.

## Oracle strategy

### Preferred oracles

- Hand-authored JSON expectations for tokenization and tree construction.
- Math-generated PNG references for simple geometry, colors, and box layout.
- Hand-authored reference PNGs for complex rendering where the exact expected
  result is stable but not easy to generate analytically.
- Reference HTML pages only when the reference uses simpler primitives already
  covered by earlier tests.
- Human-approved browser screenshots only as seed data, never as an
  automatically trusted upstream source.

### Baseline review rules

- A baseline update must include the changed actual image, old baseline, new
  baseline, diff image, manifest entries touched, and feature tags.
- Baselines generated from Chromium, Edge, Firefox, or Safari must be marked
  with the browser version and then reviewed against the HTML 5.2 requirement.
- The suite should support "quarantine" status for useful cases that currently
  fail but should still produce artifacts.

## Coverage clusters

The following clusters define the suite. Test counts are target minimums for
the first complete version; generated pairwise coverage can expand them.

| Cluster | Target | Main expectation types |
| --- | ---: | --- |
| Input, encoding, document setup | 80 | dom, render, resource-log |
| Tokenization and syntax states | 220 | tokens, dom |
| Tree construction and error recovery | 300 | dom, layout, render |
| Character references and microsyntaxes | 140 | tokens, dom, computed-style |
| Document metadata and resource selection | 90 | dom, resource-log, render |
| Element default rendering | 360 | computed-style, layout, render |
| Links, anchors, and URL resolution | 80 | dom, resource-log, semantic-outline |
| Embedded and replaced content | 180 | layout, render, resource-log |
| Tables | 160 | dom, layout, render |
| Forms and widgets | 240 | dom, layout, render, semantic-outline |
| User interaction static states | 100 | dom, layout, render |
| CSS needed by HTML rendering | 500 | computed-style, layout, render |
| Text, fonts, and internationalization | 260 | layout, display-list, render |
| Painting and compositing | 260 | display-list, render |
| XHTML and XML syntax | 100 | dom, render, negative |
| Obsolete and legacy compatibility | 160 | dom, layout, render |
| Stress, robustness, and security | 150 | negative, render, resource-log |
| Regression fixtures | Open-ended | all |

Target first complete version: about 3,380 cases before regression additions.

## Cluster detail

### 1. Input, encoding, and document setup

Goals:

- Verify document creation from inline strings, local files, and fixture URLs.
- Verify BOM and `meta charset` handling where the renderer accepts bytes.
- Verify base URL establishment for local files and HTTP fixture URLs.
- Verify quirks/no-quirks behavior if the renderer models it.
- Verify initial containing block, viewport size, canvas background, and root
  element setup.

Representative cases:

- empty input
- text-only input with implicit `html`, `head`, and `body`
- explicit `html`, `head`, `body`
- duplicate `html` or `body`
- comments and whitespace before doctype
- doctype variants: standard, legacy, malformed, missing
- charset via BOM, `meta charset`, and `http-equiv`
- local relative resource path with and without `base`

### 2. Tokenization and syntax

Goals:

- Cover the tokenizer state machine relevant to HTML documents.
- Check attributes, comments, doctypes, character references, raw text, RCDATA,
  self-closing syntax, EOF behavior, and malformed markup.
- Produce token expectation files that are easier to debug than screenshots.

Subclusters:

- data and tag-open states
- start tags, end tags, uppercase/lowercase normalization
- attribute names and values: empty, missing value, unquoted, single-quoted,
  double-quoted, duplicate attributes, whitespace, slash handling
- boolean attributes
- comments: normal, empty, abrupt close, bogus, nested-looking content
- doctypes: standard, public/system identifiers, bogus doctypes
- character references: named, numeric decimal, numeric hex, missing semicolon,
  ambiguous ampersands, invalid code points
- raw-text and RCDATA elements: `style`, `script`, `title`, `textarea`
- EOF in tag, comment, attribute, raw text, and entity

### 3. Tree construction and error recovery

Goals:

- Verify browser-compatible DOM shape for invalid and omitted markup.
- Cover insertion modes that affect static rendering.
- Separate tree errors from layout errors by asserting normalized DOM.

Subclusters:

- implicit `html`, `head`, and `body`
- head/body transitions
- omitted end tags for `p`, `li`, `dt`, `dd`, `option`, `thead`, `tbody`,
  `tfoot`, `tr`, `td`, and `th`
- nested paragraphs and block-in-paragraph behavior
- active formatting reconstruction for `b`, `i`, `em`, `strong`, `a`, and
  misnested inline elements
- adoption-agency style cases for misnested formatting
- table insertion modes, including foster parenting of stray text and blocks
- implicit `tbody`
- captions, colgroups, rows, cells, and nested tables
- forms inside tables and malformed form nesting
- `select` and `option` parsing
- `template` contents and inert subtree expectations
- foreign content transitions for inline `svg` and `math`
- fragment parsing contexts, if exposed by the renderer

### 4. Character references and microsyntaxes

Goals:

- Verify reusable parsing rules that many elements depend on.
- Avoid hiding value parsing bugs inside visual tests.

Subclusters:

- boolean attributes: present empty, present named, present arbitrary, absent
- enumerated attributes: valid, invalid, missing default, empty string
- signed and unsigned integers
- floating point, percentages, non-zero percentages, dimensions
- date, time, month, week, local date-time strings for form controls
- comma-separated and space-separated token lists
- legacy color parsing and invalid colors
- media query parsing for `style`, `link`, and responsive images
- URL parsing and percent/fragment handling as visible through resource logs

### 5. Document metadata and resources

Elements:

- `html`, `head`, `title`, `base`, `link`, `meta`, `style`

Goals:

- Verify metadata elements land in the correct tree positions.
- Verify stylesheets are discovered, filtered, loaded, and applied.
- Verify document title and metadata are represented in dumps if supported.

Subclusters:

- `base href` and `base target`
- external stylesheets via `link rel=stylesheet`
- ignored `link` types versus loaded stylesheet links
- `media` and `type` filtering
- style elements in `head` and `body`
- multiple style/link order and cascade interaction
- `meta charset` and `http-equiv` handling
- icons and non-rendering link types as resource-log-only cases

### 6. Sectioning and grouping elements

Elements:

- `body`, `article`, `section`, `nav`, `aside`, `h1` through `h6`, `header`,
  `footer`
- `p`, `address`, `hr`, `pre`, `blockquote`, `ol`, `ul`, `li`, `dl`, `dt`,
  `dd`, `figure`, `figcaption`, `main`, `div`

Goals:

- Verify default display, margins, heading sizes, list behavior, and outline
  metadata where available.
- Verify global attributes interact with these elements.

Subclusters:

- heading default font sizes/margins
- nested sectioning and headings
- multiple `main` with hidden extras
- `p` auto-closing and paragraph boundaries
- `pre` whitespace preservation
- `blockquote` margins and citation attributes
- ordered list numbering: default, `start`, `reversed`, `type`, nested
- unordered list markers and nested marker styles
- description lists
- figure and figcaption placement
- `hr` default rendering and style overrides

### 7. Text-level semantics

Elements:

- `a`, `em`, `strong`, `small`, `s`, `cite`, `q`, `dfn`, `abbr`, `ruby`, `rb`,
  `rt`, `rtc`, `rp`, `data`, `time`, `code`, `var`, `samp`, `kbd`, `sub`,
  `sup`, `i`, `b`, `u`, `mark`, `bdi`, `bdo`, `span`, `br`, `wbr`

Goals:

- Verify inline layout, font changes, decorations, quotes, ruby, bidi, and line
  breaking.

Subclusters:

- nested inline formatting
- underline, strikethrough, mark background, sub/sup metrics
- `q` generated quotes, nesting, and language-sensitive quote styles
- `br` line break behavior
- `wbr` optional break behavior
- `ruby` base/text/fallback rendering
- `bdi`, `bdo`, `dir`, and bidirectional text
- `a` default color/decoration and visited-neutral deterministic behavior
- inline replaced elements and baseline alignment

### 8. Edits

Elements:

- `ins`, `del`

Goals:

- Verify default rendering and integration inside paragraphs, lists, and tables.

Subclusters:

- inline inserted/deleted text
- block-level edits
- edits crossing paragraph/list/table boundaries
- `cite` and `datetime` attribute parsing as semantic metadata

### 9. Embedded and replaced content

Elements:

- `picture`, `source`, `img`, `iframe`, `embed`, `object`, `param`, `video`,
  `audio`, `track`, `map`, `area`, inline MathML, inline SVG, `canvas`

Goals:

- Verify static sizing, fallback behavior, intrinsic dimensions, image
  selection, and local resource loading.

Subclusters:

- `img` intrinsic size, explicit width/height, one dimension plus aspect ratio
- broken images and fallback/error placeholder behavior
- `alt` text rendering policy, if renderer supports it
- `srcset` and `sizes` static selection
- `picture` source ordering and media filtering
- raster formats: PNG, JPEG, GIF first frame, BMP if supported
- SVG image files and inline SVG
- data URLs for images and SVG
- image maps as DOM/resource/semantic tests
- `object` fallback content
- `iframe` static fallback or unsupported classification
- media element poster, controls attribute default box, and fallback content
- `track` parsing and resource-log behavior without media playback
- `canvas` fallback content and default dimensions

### 10. Links and URL resolution

Elements and attributes:

- `a`, `area`, `link`, `base`, URL-bearing attributes, `download`, `rel`,
  `target`, `hreflang`, `type`, `ping`, `referrerpolicy`

Goals:

- Verify URL resolution and static link semantics without navigation.

Subclusters:

- relative, root-relative, fragment-only, query-only, absolute, data, file, and
  invalid URLs
- `base` changes and multiple `base` elements
- link type parsing
- anchor layout and decoration
- image map coordinates as semantic metadata where available

### 11. Tables

Elements:

- `table`, `caption`, `colgroup`, `col`, `tbody`, `thead`, `tfoot`, `tr`,
  `td`, `th`

Goals:

- Verify table parsing, anonymous table objects, layout, borders, captions,
  spanning, alignment, and header associations.

Subclusters:

- implicit `tbody`
- `caption` top/bottom placement
- column groups and column styling
- auto and fixed table layout
- row and column spans
- empty cells
- border collapse and separate borders
- cell padding, spacing, baseline alignment, vertical align
- percentage widths and constrained widths
- nested tables
- malformed table content and foster parenting
- header scope and associations in semantic output

### 12. Forms and widgets

Elements:

- `form`, `label`, `input`, `button`, `select`, `datalist`, `optgroup`,
  `option`, `textarea`, `output`, `progress`, `meter`, `fieldset`, `legend`

Input states:

- `hidden`, `text`, `search`, `tel`, `url`, `email`, `password`, `date`,
  `month`, `week`, `time`, `datetime-local`, `number`, `range`, `color`,
  `checkbox`, `radio`, `file`, `submit`, `image`, `reset`, `button`

Goals:

- Verify static control rendering, labels, disabled/read-only/checked/selected
  states, values, default values, and form association metadata.

Subclusters:

- each input type with default, value, disabled, readonly where applicable
- checkbox/radio checked and unchecked states
- text-like controls with `value`, `placeholder`, `size`, `maxlength`
- date/time/number/range/color static boxes
- image submit button resource behavior
- `button` content and type
- `select` single and multiple, selected option, optgroup
- `textarea` text content, wrapping, placeholder
- `datalist` as non-rendered suggestion source
- `progress` and `meter` value/min/max/low/high/optimum
- `fieldset` and `legend` geometry
- external form ownership via `form` attribute in semantic dumps
- constraint attributes as metadata, not live validation UI

### 13. User interaction static states

Features:

- `hidden`, inert subtrees, focusability metadata, `tabindex`, `accesskey`,
  `contenteditable`, `spellcheck`, `draggable`, `details`, `summary`, `dialog`

Goals:

- Test static visual and semantic states without event simulation.

Subclusters:

- `hidden` suppresses rendering
- hidden sectioning and multiple `main`
- `details` closed and `details open`
- `summary` default marker and custom content
- `dialog` with and without `open`
- `tabindex` parsing in semantic dumps
- `contenteditable`, `spellcheck`, `draggable`, `accesskey` as metadata

### 14. Scripting-related elements without script execution

Elements:

- `script`, `noscript`, `template`, `canvas`

Goals:

- Verify plain rendering behavior when scripting is unavailable.

Subclusters:

- `script` raw-text tokenization
- script element is not visually rendered
- `noscript` content appears when scripting is disabled
- `template` contents are parsed but inert and not rendered
- `canvas` fallback renders when no canvas bitmap exists
- canvas width/height attributes affect fallback box sizing if applicable

### 15. CSS required for competitive HTML rendering

This is not a full CSS conformance suite, but HTML rendering cannot be
competitive without CSS coverage. These tests should be marked as
`css-required-by-html`.

Subclusters:

- CSS syntax: style attributes, style elements, external stylesheets,
  comments, escapes, invalid declarations
- cascade: origin, order, specificity, inheritance, initial/unset behavior
- selectors: type, class, ID, attribute, descendant, child, sibling, grouping,
  pseudo-classes relevant to static state
- units: px, em, rem, percentages, absolute lengths, zero, calc if supported
- colors: named, hex, rgb/rgba, transparent, currentColor
- display: block, inline, inline-block, none, table display values, list-item
- box model: margin, border, padding, width, height, min/max
- normal flow: block formatting, inline formatting, whitespace, line boxes
- floats and clear
- positioning: relative, absolute, fixed if supported, z-index
- overflow and clipping
- lists and counters
- generated content for quotes and list markers where supported
- typography: font family, weight, style, stretch if supported, size,
  line-height, text-align, text-indent, white-space, text-decoration
- backgrounds: color, image, repeat, position, size, origin, clip, multiple
  layers
- borders: width/style/color, radius, collapsed table borders
- outlines
- opacity and stacking contexts
- transforms and filters only if the renderer claims support
- media queries for `screen`, viewport width, and print-like targets if
  supported
- `@font-face` loading from local fixture files

### 16. Text, fonts, and internationalization

Goals:

- Verify text shaping, fallback, metrics, bidi, line breaking, and language
  effects.

Subclusters:

- deterministic Latin fixture font
- existing OFL fonts such as Vazirmatn for Arabic/Persian shaping coverage
- CJK fallback and line breaking
- Hebrew/Arabic bidi with neutral punctuation and numbers
- combining marks and accents
- emoji fallback and missing glyph behavior
- ligatures if supported
- tab, newline, multiple spaces, non-breaking space
- hyphenation only if renderer supports it
- language-sensitive quotes and text transforms where supported
- vertical text only if renderer claims support

### 17. Painting and compositing

Goals:

- Verify paint order and raster output independent of tree correctness.

Subclusters:

- canvas and root/body background propagation
- background painting areas
- border painting order and joins
- rounded corners and clipping
- inline background fragmentation
- text painting and decorations
- list markers
- replaced element painting
- table background layers: table, colgroup, col, row group, row, cell
- stacking contexts and z-index
- opacity
- shadows if supported
- image interpolation and scaling
- antialiasing tolerance classification

### 18. XHTML and XML syntax

Goals:

- Verify the XML syntax path separately from HTML recovery behavior.

Subclusters:

- well-formed XHTML document
- lowercase names and explicit closing tags
- XML namespaces for XHTML, SVG, and MathML
- XML entity handling
- CDATA in SVG/MathML contexts
- malformed XML negative cases
- self-closing syntax behavior in XHTML versus HTML

### 19. Obsolete and legacy compatibility

Goals:

- Track non-conforming but renderer-relevant markup found in old content.
- Keep these separate from conforming HTML 5.2 cases.

Subclusters:

- obsolete presentational elements: `center`, `font`, `strike`, `tt`, `big`
- frameset-era elements: `frameset`, `frame`, `noframes`
- legacy text containers: `xmp`, `listing`, `plaintext`
- obsolete attributes: `align`, `bgcolor`, `border`, `cellpadding`,
  `cellspacing`, `valign`, `width`, `height`, `hspace`, `vspace`
- legacy color names and numeric colors
- `marquee` static fallback or unsupported classification

### 20. Stress, robustness, and security

Goals:

- Ensure malformed or large documents do not crash, hang, or escape the
  resource sandbox.

Subclusters:

- very deep nesting
- very wide sibling lists
- huge attribute values
- many stylesheets
- many images
- cyclic resource references
- data URL size limits
- malformed CSS recovery
- invalid image bytes
- unsupported image types
- XML entity expansion denial cases
- path traversal attempts in resource URLs
- timeout enforcement
- deterministic output repeated N times

## Coverage matrix rules

Each coverage item should include:

- `specSection`
- `featureId`
- `cluster`
- `requiredForStaticRenderer`: true or false
- `supportLevel`: `required`, `recommended`, `legacy`, `optional`,
  `out-of-scope`
- `tests`: list of manifest IDs
- `notes`

Every HTML 5.2 element gets at least these coverage rows:

- parser placement
- content model or malformed recovery that matters to rendering
- default display/computed style
- global attribute interaction: `id`, `class`, `style`, `hidden`, `lang`,
  `dir`, `title`, and `data-*`
- relevant element-specific attributes
- static visual rendering
- semantic dump if supported
- unsupported classification if the element has browser behavior outside a
  static renderer

Every CSS feature used by a reference page must itself be covered by simpler
primitive tests. This prevents reference pages from silently depending on
untested behavior.

## Test authoring guidelines

- Prefer one assertion per test file unless the assertions are inseparable.
- Use small fixed viewports: commonly 320x240, 640x480, 800x600, and 1024x768.
- Use deterministic fixture fonts, not host fonts, for pixel-sensitive tests.
- Keep visual assertions simple: solid colors, known dimensions, no unnecessary
  antialiasing, and high contrast.
- Use CSS only when it is the feature under test or needed to expose HTML
  behavior.
- Avoid decorative text in visual tests; text changes create noisy diffs.
- Include human-readable text labels only when they help diagnose a failure and
  are not the measured output.
- Keep all resources local to the fixture root.
- Mark tests involving platform-native controls as looser tolerance or
  structure-only unless the renderer owns the widget drawing.
- Add regression tests beside the cluster that owns the behavior, not in a
  permanent miscellaneous bucket.

## Generation strategy

Generated tests should cover combinatorial areas without creating a maintenance
trap.

Use generators for:

- global attributes across representative element categories
- boolean and enumerated attributes
- input types and common states
- table span/layout combinations
- image dimension combinations
- character reference tables
- parser EOF/error cases
- CSS cascade/specificity matrices
- URL resolution matrices

Use hand-authored tests for:

- tricky tree-construction recovery
- bidi and complex text
- form control visual expectations
- legacy compatibility
- stress and security cases
- any case where the spec prose is subtle

Use pairwise or targeted combinatorics, not exhaustive Cartesian products,
unless the generated output remains small and easy to review.

## CI plan

Suggested stages:

1. Manifest and coverage validation.
2. Fast parser-only suite.
3. Fast rendering smoke suite across all clusters.
4. Full visual suite on at least one deterministic backend.
5. Stress suite with stricter timeouts and no baseline updates.
6. Optional platform comparison jobs for WPF, image backend, and
   Broiler.Graphics backend.

Artifacts:

- `summary.json`
- `summary.md`
- `coverage.md`
- per-case actual, expected, diff, JSON dumps, and resource logs
- top failure signatures grouped by cluster and mismatch category

Failure policy:

- Required tests fail CI.
- Quarantined tests run and publish artifacts but do not fail CI.
- Optional/legacy tests can be tracked separately until the renderer declares
  support.
- Any crash, timeout, or resource sandbox violation fails CI even if the
  feature is optional.

## Implementation phases

### Phase 0: Planning and harness skeleton

Deliverables:

- this planning document
- `tests/html52/README.md`
- manifest schema
- empty coverage maps
- runner skeleton with manifest validation and summary output

Acceptance criteria:

- `npm run html52:coverage` reports uncovered planned features.
- `npm run html52:run -- --dry-run` lists selected cases.

### Phase 1: Parser and DOM suite

Deliverables:

- tokenizer and DOM dump commands
- syntax and tree-construction fixtures
- character reference generator
- parser-only CI job

Acceptance criteria:

- at least 500 parser/DOM cases
- all cases script-independent
- every failure includes actual and expected JSON

### Phase 2: Core visual rendering suite

Deliverables:

- deterministic font bundle policy
- render baseline format
- image comparison runner integration
- first sectioning, grouping, inline, list, and root/body rendering cases

Acceptance criteria:

- at least 400 visual cases
- stable repeated output on the primary backend
- per-case diff artifacts

### Phase 3: Tables, forms, and widgets

Deliverables:

- table layout coverage
- static forms and widgets coverage
- semantic form association dumps if supported

Acceptance criteria:

- all HTML 5.2 input states represented
- table parsing and layout failures separated in reports

### Phase 4: Resources and replaced content

Deliverables:

- local resource sandbox
- resource-log expectations
- image, SVG, picture/source, media fallback, object, iframe, canvas fallback
  coverage

Acceptance criteria:

- no network access during tests
- broken and unsupported resources classified predictably

### Phase 5: CSS, text, and painting depth

Deliverables:

- CSS-required-by-HTML coverage
- bidi/ruby/CJK/Arabic/combining text coverage
- paint order and stacking tests

Acceptance criteria:

- references use only CSS primitives covered by earlier tests
- antialiasing-sensitive cases have documented tolerances

### Phase 6: XHTML, legacy, stress, and security

Deliverables:

- XHTML/XML tests
- obsolete markup tests
- stress and sandbox cases
- deterministic repeat runner

Acceptance criteria:

- no crashes or hangs in stress suite
- coverage report has no unclassified HTML 5.2 renderer feature

### Phase 7: Coverage completion and maintenance

Deliverables:

- coverage dashboard in `docs/compliance.md` or a linked generated artifact
- contributor guide for adding cases
- baseline review workflow
- quarantine expiry policy

Acceptance criteria:

- every HTML 5.2 renderer-relevant coverage row is covered or explicitly
  classified
- CI runs a meaningful smoke subset on every PR and the full suite on schedule

## Risks and mitigations

| Risk | Mitigation |
| --- | --- |
| "Complete" grows without bound | Define completeness as coverage matrix closure, not infinite examples. |
| Pixel baselines are brittle | Use JSON oracles for parser/layout where possible and simple generated images for geometry. |
| Native controls differ by platform | Prefer renderer-owned widget drawing or semantic/layout assertions with looser image checks. |
| Browser-generated baselines copy browser bugs | Treat browser output as seed data requiring spec review. |
| CSS suite becomes a full CSS standards project | Mark CSS cases as required-by-HTML and keep broader CSS modules separate. |
| Generated cases become unreadable | Keep metadata concise, emit stable names, and require generated previews in artifacts. |
| Unsupported features create permanent red CI | Use required/optional/legacy/quarantine statuses with explicit ownership and expiry. |
| No-JS rule hides script-adjacent parse bugs | Include inert `script`, `noscript`, `template`, and `canvas` fallback tests without execution. |

## Immediate next implementation steps

1. Add `tests/html52/manifest.schema.json` and a minimal `README.md`.
2. Add `scripts/html52/validate-manifest.mjs` and wire an `html52:validate`
   npm script.
3. Add the first 20 parser/DOM cases for implicit structure, attributes,
   comments, raw text, and `p` implied end tags.
4. Expose a `dump-dom` command from `Broiler.HTML.Tool`.
5. Add first visual smoke cases for root/body background, headings,
   paragraphs, lists, `hidden`, `img`, and a simple table.
6. Add `scripts/html52/summarize-coverage.mjs` with all clusters marked
   planned, covered, blocked, or out-of-scope.

Once those steps exist, the rest of the suite can grow cluster by cluster
without depending on WPT or JavaScript execution.
