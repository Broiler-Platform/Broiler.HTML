# Broiler.HTML compliance tracking

## Current approach

Broiler.HTML already includes deterministic image rendering, font-loading hooks for fixture fonts such as Ahem, per-pixel comparison, and mismatch classification. The repository now also includes a Playwright-based WPT runner that can execute a local non-JS subset against Chromium and produce diff artifacts, while public suites continue to be tracked here with an explicit status and reason.

## Public compliance suites

| Suite | Link | Scope | Current status | Explicit reason / next step |
| --- | --- | --- | --- | --- |
| Web Platform Tests (WPT) | https://github.com/web-platform-tests/wpt | Broad HTML/CSS/web-platform interoperability | Partially automated in-repo | `scripts/wpt/prepare-wpt.mjs` prepares a checkout and `scripts/wpt/run-non-js.mjs` can now inventory the full non-JS corpus, apply a documented exclusion manifest, and run a focused render/diff batch. `.github/workflows/wpt-non-js.yml` publishes both the full non-JS inventory summary and the focused visual-comparison artifacts. |
| WPT live results | https://wpt.fyi/ | Published interoperability results | Referenced only | Useful as an external comparison target once Broiler.HTML starts publishing suite results. |
| CSS 2.1 test suite | https://test.csswg.org/suites/css2.1/20110323/html4/ | CSS 2.1 rendering conformance | Skipped in current repo snapshot | No checked-in harness or baseline-image corpus exists yet. |
| Acid3 | http://acid3.acidtests.org/ | Historical HTML/CSS/DOM renderer milestone test | Tracked manually | Source comments reference Acid3-related work, but the repository does not currently contain the corresponding test assets or automated checks. |
| html5lib tests | https://github.com/html5lib/html5lib-tests | HTML tokenizer/tree-construction parsing compliance | Skipped in current repo snapshot | The repository does not currently include an adapter that imports the html5lib corpus into automated parser tests. |

## Repository-supported compliance workflow

The repeatable in-repo workflow is:

1. Build the solution and install the Playwright dependency/browser (`npm install` and `npm run wpt:install-browsers`).
2. Prepare a local checkout with `scripts/wpt/prepare-wpt.mjs` (clone the official repo or copy an existing tree).
3. Let the runner skip JS-dependent files, optionally apply the checked-in exclusion manifest (`scripts/wpt/non-js-exclusions.json`) plus any ad-hoc `--exclude` filters, and either inventory the selected corpus (`--scan-only`) or render each selected case through `Broiler.HTML.Tool` before capturing a Chromium screenshot with JavaScript disabled.
4. Compare the output against the reference image with `PixelDiffRunner.Compare(...)`.
5. Use `MismatchClassifier.Classify(...)`, the generated `summary.json` / `summary.md` files, and `scripts/wpt/analyze-summary.py` to triage failures and track regressions locally or through `.github/workflows/wpt-non-js.yml`.

## Non-JS WPT exclusions

The checked-in exclusion manifest at `scripts/wpt/non-js-exclusions.json` is the source of truth for non-JS WPT cases that are intentionally skipped from the focused render/diff batch. Each entry records the exact upstream WPT path, whether the exclusion is due to an unsupported feature or an unstable case, and a short note explaining why the case is still excluded.

The CI workflow now uses that manifest in two ways:

1. `--scan-only` inventories the entire discoverable non-JS WPT corpus from the prepared upstream checkout and writes a summary artifact that lists the selected in-scope test set after exclusions.
2. The focused render/diff step reuses the same manifest so the checked-in documentation and the executed exclusions cannot drift apart silently.

<!-- BEGIN: non-js-wpt-exclusions -->
| Test path | Category | Feature / aspect | Reason for exclusion |
| --- | --- | --- | --- |
| `css/css-backgrounds/background-attachment-fixed-inside-transform-1.html` | unsupported | background-attachment fixed/local edge cases | Broiler.HTML still mismatches Chromium on fixed/local attachment positioning and related root-scroller propagation cases. |
| `css/css-backgrounds/background-attachment-fixed-border-radius-offset.html` | unsupported | background-attachment fixed/local edge cases | Broiler.HTML still mismatches Chromium on fixed/local attachment positioning and related root-scroller propagation cases. |
| `css/css-backgrounds/background-attachment-fixed.html` | unsupported | background-attachment fixed/local edge cases | Broiler.HTML still mismatches Chromium on fixed/local attachment positioning and related root-scroller propagation cases. |
| `css/css-backgrounds/background-attachment-350.html` | unsupported | background-attachment fixed/local edge cases | Broiler.HTML still mismatches Chromium on fixed/local attachment positioning and related root-scroller propagation cases. |
| `css/css-backgrounds/background-attachment-local.html` | unsupported | background-attachment fixed/local edge cases | Broiler.HTML still mismatches Chromium on fixed/local attachment positioning and related root-scroller propagation cases. |
| `css/css-backgrounds/background-attachment-local-hidden.html` | unsupported | background-attachment fixed/local edge cases | Broiler.HTML still mismatches Chromium on fixed/local attachment positioning and related root-scroller propagation cases. |
| `css/css-backgrounds/background-attachment-local-scrolling.htm` | unsupported | background-attachment fixed/local edge cases | Broiler.HTML still mismatches Chromium on fixed/local attachment positioning and related root-scroller propagation cases. |
| `css/css-backgrounds/background-attachment-margin-root-001.html` | unsupported | background-attachment fixed/local edge cases | Broiler.HTML still mismatches Chromium on fixed/local attachment positioning and related root-scroller propagation cases. |
| `css/css-backgrounds/background-attachment-margin-root-002.html` | unsupported | background-attachment fixed/local edge cases | Broiler.HTML still mismatches Chromium on fixed/local attachment positioning and related root-scroller propagation cases. |
| `css/css-backgrounds/background-334.html` | unstable | background painting edge case | This case still produces intermittent visual mismatches in CI and remains excluded until the underlying renderer gap is isolated. |
| `css/css-backgrounds/background-color-applied-to-rounded-inline-element.htm` | unsupported | rounded inline background painting | Inline background painting with rounded corners does not yet match Chromium. |
| `css/css-backgrounds/background-color-body-propagation-001.html` | unsupported | body-to-root background propagation | Body-to-root background color propagation still mismatches Chromium. |
| `css/css-backgrounds/background-color-body-propagation-002.html` | unsupported | body-to-root background propagation | Body-to-root background color propagation still mismatches Chromium. |
| `css/css-backgrounds/animations/background-color-animation-field-crash.html` | unsupported | background-color animations | CSS background-color animations are not implemented yet, so these animation snapshots still mismatch Chromium. |
| `css/css-backgrounds/animations/background-color-animation-in-body.html` | unsupported | background-color animations | CSS background-color animations are not implemented yet, so these animation snapshots still mismatch Chromium. |
| `css/css-backgrounds/animations/background-color-animation-with-images.html` | unsupported | background-color animations | CSS background-color animations are not implemented yet, so these animation snapshots still mismatch Chromium. |
| `css/css-backgrounds/background-clip-002.html` | unsupported | background-clip box geometry | background-clip box geometry and rounded-corner propagation are not fully implemented yet. |
| `css/css-backgrounds/background-clip-003.html` | unsupported | background-clip box geometry | background-clip box geometry and rounded-corner propagation are not fully implemented yet. |
| `css/css-backgrounds/background-clip-004.html` | unsupported | background-clip box geometry | background-clip box geometry and rounded-corner propagation are not fully implemented yet. |
| `css/css-backgrounds/background-clip-005.html` | unsupported | background-clip box geometry | background-clip box geometry and rounded-corner propagation are not fully implemented yet. |
| `css/css-backgrounds/background-clip-006.html` | unsupported | background-clip box geometry | background-clip box geometry and rounded-corner propagation are not fully implemented yet. |
| `css/css-backgrounds/background-clip-007.html` | unsupported | background-clip box geometry | background-clip box geometry and rounded-corner propagation are not fully implemented yet. |
| `css/css-backgrounds/background-clip-008.html` | unsupported | background-clip box geometry | background-clip box geometry and rounded-corner propagation are not fully implemented yet. |
| `css/css-backgrounds/background-clip-009.html` | unsupported | background-clip box geometry | background-clip box geometry and rounded-corner propagation are not fully implemented yet. |
| `css/css-backgrounds/background-clip-010.html` | unsupported | background-clip box geometry | background-clip box geometry and rounded-corner propagation are not fully implemented yet. |
| `css/css-backgrounds/background-clip-color.html` | unsupported | background-clip box geometry | background-clip box geometry and rounded-corner propagation are not fully implemented yet. |
| `css/css-backgrounds/background-clip-content-box.html` | unsupported | background-clip box geometry | background-clip box geometry and rounded-corner propagation are not fully implemented yet. |
| `css/css-backgrounds/background-clip-content-box-001.html` | unsupported | background-clip box geometry | background-clip box geometry and rounded-corner propagation are not fully implemented yet. |
| `css/css-backgrounds/background-clip-content-box-002.html` | unsupported | background-clip box geometry | background-clip box geometry and rounded-corner propagation are not fully implemented yet. |
| `css/css-backgrounds/background-clip-content-box-with-border-radius-002.html` | unsupported | background-clip box geometry | background-clip box geometry and rounded-corner propagation are not fully implemented yet. |
| `css/css-backgrounds/background-clip-content-box-with-border-radius-003.html` | unsupported | background-clip box geometry | background-clip box geometry and rounded-corner propagation are not fully implemented yet. |
| `css/css-backgrounds/background-clip-padding-box-001.html` | unsupported | background-clip box geometry | background-clip box geometry and rounded-corner propagation are not fully implemented yet. |
| `css/css-backgrounds/background-clip-padding-box-with-border-radius.html` | unsupported | background-clip box geometry | background-clip box geometry and rounded-corner propagation are not fully implemented yet. |
| `css/css-backgrounds/background-clip-padding-box-with-border-radius-002.html` | unsupported | background-clip box geometry | background-clip box geometry and rounded-corner propagation are not fully implemented yet. |
| `css/css-backgrounds/background-clip-padding-box-with-border-radius-003.html` | unsupported | background-clip box geometry | background-clip box geometry and rounded-corner propagation are not fully implemented yet. |
| `css/css-backgrounds/background-clip-root.html` | unsupported | background-clip box geometry | background-clip box geometry and rounded-corner propagation are not fully implemented yet. |
| `css/css-backgrounds/background-clip_padding-box.html` | unsupported | background-clip box geometry | background-clip box geometry and rounded-corner propagation are not fully implemented yet. |
| `css/css-backgrounds/background_color_padding_box.htm` | unsupported | background-clip box geometry | background-clip box geometry and rounded-corner propagation are not fully implemented yet. |
| `css/css-backgrounds/background-clip/clip-border-area-border-image.html` | unsupported | background-clip:border-area | background-clip:border-area and its related border-shape variations are not implemented yet. |
| `css/css-backgrounds/background-clip/clip-border-area-border-shape-background-position.html` | unsupported | background-clip:border-area | background-clip:border-area and its related border-shape variations are not implemented yet. |
| `css/css-backgrounds/background-clip/clip-border-area-border-shape-overflow.html` | unsupported | background-clip:border-area | background-clip:border-area and its related border-shape variations are not implemented yet. |
| `css/css-backgrounds/background-clip/clip-border-area-border-shape.html` | unsupported | background-clip:border-area | background-clip:border-area and its related border-shape variations are not implemented yet. |
| `css/css-backgrounds/background-clip/clip-border-box_with_position.html` | unsupported | background-clip box geometry | Complex background-clip box geometry with radius, position, and size variations still mismatches Chromium. |
| `css/css-backgrounds/background-clip/clip-border-box_with_radius.html` | unsupported | background-clip box geometry | Complex background-clip box geometry with radius, position, and size variations still mismatches Chromium. |
| `css/css-backgrounds/background-clip/clip-border-box_with_size.html` | unsupported | background-clip box geometry | Complex background-clip box geometry with radius, position, and size variations still mismatches Chromium. |
| `css/css-backgrounds/background-clip/clip-border-box.html` | unsupported | background-clip box geometry | Complex background-clip box geometry with radius, position, and size variations still mismatches Chromium. |
| `css/css-backgrounds/background-clip/clip-border-shape-table-part-background.html` | unsupported | table-part background clipping | Table-part background clipping with border-shape interaction still mismatches Chromium. |
| `css/css-backgrounds/background-clip/clip-content-box.html` | unsupported | background-clip box geometry | Complex background-clip box geometry with radius, position, and size variations still mismatches Chromium. |
| `css/css-backgrounds/background-clip/clip-content-box_with_radius.html` | unsupported | background-clip box geometry | Complex background-clip box geometry with radius, position, and size variations still mismatches Chromium. |
| `css/css-backgrounds/background-clip/clip-content-box_with_position.html` | unsupported | background-clip box geometry | Complex background-clip box geometry with radius, position, and size variations still mismatches Chromium. |
| `css/css-backgrounds/background-clip/clip-content-box_with_size.html` | unsupported | background-clip box geometry | Complex background-clip box geometry with radius, position, and size variations still mismatches Chromium. |
| `css/css-backgrounds/background-clip/clip-padding-box.html` | unsupported | background-clip box geometry | Complex background-clip box geometry with radius, position, and size variations still mismatches Chromium. |
| `css/css-backgrounds/background-clip/clip-padding-box_with_position.html` | unsupported | background-clip box geometry | Complex background-clip box geometry with radius, position, and size variations still mismatches Chromium. |
| `css/css-backgrounds/background-clip/clip-padding-box_with_radius.html` | unsupported | background-clip box geometry | Complex background-clip box geometry with radius, position, and size variations still mismatches Chromium. |
| `css/css-backgrounds/background-clip/clip-padding-box_with_size.html` | unsupported | background-clip box geometry | Complex background-clip box geometry with radius, position, and size variations still mismatches Chromium. |
| `css/css-backgrounds/background-clip/clip-border-area-box-decoration-break.html` | unsupported | background-clip:border-area | background-clip:border-area and its related border-shape variations are not implemented yet. |
| `css/css-backgrounds/background-clip/clip-border-area.html` | unsupported | background-clip:border-area | background-clip:border-area and its related border-shape variations are not implemented yet. |
| `css/css-backgrounds/background-clip/clip-border-area-on-body-not-propagated-to-root.html` | unsupported | background-clip:border-area | background-clip:border-area and its related border-shape variations are not implemented yet. |
| `css/css-backgrounds/background-clip/clip-border-area-corner-shape.html` | unsupported | background-clip:border-area | background-clip:border-area and its related border-shape variations are not implemented yet. |
| `css/css-backgrounds/background-clip/clip-border-area-text.html` | unsupported | background-clip:border-area | background-clip:border-area and its related border-shape variations are not implemented yet. |
| `css/css-backgrounds/background-clip/clip-rounded-corner.html` | unsupported | background-clip box geometry | Complex background-clip box geometry with radius, position, and size variations still mismatches Chromium. |
| `css/css-backgrounds/background-clip/clip-text-background-table-cell.html` | unsupported | background-clip:text | background-clip:text and its inline, fragmentation, and body-propagation variants are not supported yet. |
| `css/css-backgrounds/background-clip/clip-text-descendants.html` | unsupported | background-clip:text | background-clip:text and its inline, fragmentation, and body-propagation variants are not supported yet. |
| `css/css-backgrounds/background-clip/clip-text-ellipsis.html` | unsupported | background-clip:text | background-clip:text and its inline, fragmentation, and body-propagation variants are not supported yet. |
| `css/css-backgrounds/background-clip/clip-text-flex.html` | unsupported | background-clip:text | background-clip:text and its inline, fragmentation, and body-propagation variants are not supported yet. |
| `css/css-backgrounds/background-clip/clip-text-inline.html` | unsupported | background-clip:text | background-clip:text and its inline, fragmentation, and body-propagation variants are not supported yet. |
| `css/css-backgrounds/background-clip/clip-text-inline-block-child.html` | unsupported | background-clip:text | background-clip:text and its inline, fragmentation, and body-propagation variants are not supported yet. |
| `css/css-backgrounds/background-clip/clip-text-multi-line.html` | unsupported | background-clip:text | background-clip:text and its inline, fragmentation, and body-propagation variants are not supported yet. |
| `css/css-backgrounds/background-clip/clip-text-multiline-linebreak.html` | unsupported | background-clip:text | background-clip:text and its inline, fragmentation, and body-propagation variants are not supported yet. |
| `css/css-backgrounds/background-clip/clip-text-multiline-background-image.html` | unsupported | background-clip:text | background-clip:text and its inline, fragmentation, and body-propagation variants are not supported yet. |
| `css/css-backgrounds/background-clip/clip-text-out-of-flow-child.html` | unsupported | background-clip:text | background-clip:text and its inline, fragmentation, and body-propagation variants are not supported yet. |
| `css/css-backgrounds/background-clip/clip-text-stacking-context-child.html` | unsupported | background-clip:text | background-clip:text and its inline, fragmentation, and body-propagation variants are not supported yet. |
| `css/css-backgrounds/background-clip/clip-text-relative-child.html` | unsupported | background-clip:text | background-clip:text and its inline, fragmentation, and body-propagation variants are not supported yet. |
| `css/css-backgrounds/background-clip/clip-text-text-align.html` | unsupported | background-clip:text | background-clip:text and its inline, fragmentation, and body-propagation variants are not supported yet. |
| `css/css-backgrounds/background-clip/clip-text-text-decorations.html` | unsupported | background-clip:text | background-clip:text and its inline, fragmentation, and body-propagation variants are not supported yet. |
| `css/css-backgrounds/background-clip/clip-text-text-emphasis.html` | unsupported | background-clip:text | background-clip:text and its inline, fragmentation, and body-propagation variants are not supported yet. |
| `css/css-backgrounds/background-clip/clip-text-transform.html` | unsupported | background-clip:text | background-clip:text and its inline, fragmentation, and body-propagation variants are not supported yet. |
| `css/css-backgrounds/background-clip/clip-text-on-body-not-propagated-to-root.html` | unsupported | background-clip:text | background-clip:text and its inline, fragmentation, and body-propagation variants are not supported yet. |
| `css/css-backgrounds/background-clip/clip-text-scaled.html` | unsupported | background-clip:text | background-clip:text and its inline, fragmentation, and body-propagation variants are not supported yet. |
| `css/css-backgrounds/background-clip/clip-text-constrain-geometry.html` | unsupported | background-clip:text | background-clip:text and its inline, fragmentation, and body-propagation variants are not supported yet. |
| `css/css-backgrounds/background-clip/clip-text-fragmentation.html` | unsupported | background-clip:text | background-clip:text and its inline, fragmentation, and body-propagation variants are not supported yet. |
| `css/css-backgrounds/background-clip/clip-text-blend-mode.html` | unsupported | background-clip:text | background-clip:text and its inline, fragmentation, and body-propagation variants are not supported yet. |
| `css/css-backgrounds/background_properties_greater_than_images.htm` | unsupported | background layer list expansion | Layer list expansion when background properties outnumber background images still mismatches Chromium. |
| `css/css-backgrounds/background_repeat_space_border_box.htm` | unsupported | background-repeat: space | background-repeat: space is not supported yet. |
| `css/css-backgrounds/background_repeat_space_content_box.htm` | unsupported | background-repeat: space | background-repeat: space is not supported yet. |
<!-- END: non-js-wpt-exclusions -->

## Status publication

This document is the repository's source of truth for public-suite tracking:

- which public suites are tracked
- whether each suite is automated, manual, or skipped
- why a suite is skipped when it is not yet integrated

As checked-in suite fixtures and runners are added, this table should be updated from `Tracked` / `Skipped` to `Passing`, `Failing`, or `Partially passing` with links to the relevant test assets and results. The non-JS WPT exclusion table above should also be kept in sync with the upstream WPT snapshot and the checked-in exclusion manifest.
