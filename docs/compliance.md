# Broiler.HTML compliance tracking

## Current approach

Broiler.HTML already includes deterministic image rendering, font-loading hooks for fixture fonts such as Ahem, per-pixel comparison, and mismatch classification. The repository now also includes a Playwright-based WPT runner that can execute a local non-JS subset against Chromium and produce diff artifacts, while public suites continue to be tracked here with an explicit status and reason.

## Public compliance suites

| Suite | Link | Scope | Current status | Explicit reason / next step |
| --- | --- | --- | --- | --- |
| Web Platform Tests (WPT) | https://github.com/web-platform-tests/wpt | Broad HTML/CSS/web-platform interoperability | Partially automated in-repo | `scripts/wpt/prepare-wpt.mjs` prepares a checkout and `scripts/wpt/run-non-js.mjs` runs it through the Broiler CLI and Chromium/Playwright with JavaScript disabled, then writes image diffs and JSON/Markdown summaries. `.github/workflows/wpt-non-js.yml` runs a curated subset in CI and uploads the resulting artifacts. |
| WPT live results | https://wpt.fyi/ | Published interoperability results | Referenced only | Useful as an external comparison target once Broiler.HTML starts publishing suite results. |
| CSS 2.1 test suite | https://test.csswg.org/suites/css2.1/20110323/html4/ | CSS 2.1 rendering conformance | Skipped in current repo snapshot | No checked-in harness or baseline-image corpus exists yet. |
| Acid3 | http://acid3.acidtests.org/ | Historical HTML/CSS/DOM renderer milestone test | Tracked manually | Source comments reference Acid3-related work, but the repository does not currently contain the corresponding test assets or automated checks. |
| html5lib tests | https://github.com/html5lib/html5lib-tests | HTML tokenizer/tree-construction parsing compliance | Skipped in current repo snapshot | The repository does not currently include an adapter that imports the html5lib corpus into automated parser tests. |

## Repository-supported compliance workflow

The repeatable in-repo workflow is:

1. Build the solution and install the Playwright dependency/browser (`npm install` and `npm run wpt:install-browsers`).
2. Prepare a local checkout with `scripts/wpt/prepare-wpt.mjs` (clone the official repo or copy an existing tree).
3. Let the runner skip JS-dependent files, optionally exclude known unstable cases with repeated `--exclude` filters, render each selected case through `Broiler.HTML.Tool`, and capture a Chromium screenshot with JavaScript disabled.
4. Compare the output against the reference image with `PixelDiffRunner.Compare(...)`.
5. Use `MismatchClassifier.Classify(...)`, the generated `summary.json` / `summary.md` files, and `scripts/wpt/analyze-summary.py` to triage failures and track regressions locally or through `.github/workflows/wpt-non-js.yml`.

## Status publication

This document is the repository's source of truth for public-suite tracking:

- which public suites are tracked
- whether each suite is automated, manual, or skipped
- why a suite is skipped when it is not yet integrated

As checked-in suite fixtures and runners are added, this table should be updated from `Tracked` / `Skipped` to `Passing`, `Failing`, or `Partially passing` with links to the relevant test assets and results.
