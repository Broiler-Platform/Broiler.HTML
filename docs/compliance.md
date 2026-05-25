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

There are currently no excluded non-JS WPT cases.

<!-- BEGIN: non-js-wpt-exclusions -->
| Test path | Category | Feature / aspect | Reason for exclusion |
| --- | --- | --- | --- |
<!-- END: non-js-wpt-exclusions -->

### Newly enabled features and testcases

The following CSS background features and their corresponding WPT testcases were previously excluded and are now enabled:

| Feature | Test count | Implementation notes |
| --- | --- | --- |
| background-attachment fixed/local edge cases | 9 | Fixed and local attachment positioning is implemented in `PaintWalker.EmitBackgroundImageLayer`. |
| background-clip box geometry (padding-box, content-box, border-box) | 35 | Clip rectangle computation is implemented in `PaintWalker.GetBackgroundClipRect` and rounded-corner clipping in `TryCreateRoundedBackgroundClipItem`. |
| background-clip: border-area | 9 | Border-area rendering is implemented via `PaintWalker.EmitBorderAreaBorder`. A bug in `GetEffectiveBackgroundClip` that incorrectly mapped `border-area` to `border-box` (making the border-area code path unreachable) was fixed as part of this change. |
| background-clip: text | 21 | Text clip detection suppresses normal background painting and propagates the background color to descendant text shapes. |
| background-repeat: space | 2 | Space repeat tiling is implemented in `RGraphicsRasterBackend.DrawSpace` with even gap distribution. |
| body-to-root background propagation | 2 | Canvas background propagation follows CSS2.1 §14.2 in `PaintWalker.EmitCanvasBackground`. |
| rounded inline background painting | 1 | Inline elements use per-line-box paint rects with rounded-corner clipping. |
| table-part background clipping | 1 | Table-part backgrounds are clipped using the standard background-clip pipeline. |
| background layer list expansion | 1 | Multi-layer background properties use modular indexing when property lists exceed the image count. |
| background painting edge case | 1 | Previously marked as unstable; now included in the standard test run. |

## Status publication

This document is the repository's source of truth for public-suite tracking:

- which public suites are tracked
- whether each suite is automated, manual, or skipped
- why a suite is skipped when it is not yet integrated

As checked-in suite fixtures and runners are added, this table should be updated from `Tracked` / `Skipped` to `Passing`, `Failing`, or `Partially passing` with links to the relevant test assets and results. The non-JS WPT exclusion table above should also be kept in sync with the upstream WPT snapshot and the checked-in exclusion manifest.
