# Broiler.HTML compliance tracking

## Current approach

Broiler.HTML already includes deterministic image rendering, font-loading hooks for fixture fonts such as Ahem, per-pixel comparison, and mismatch classification. This repository does not yet contain checked-in public-suite fixtures or a dedicated compliance test project, so public suites are tracked here with an explicit status and reason.

## Public compliance suites

| Suite | Link | Scope | Current status | Explicit reason / next step |
| --- | --- | --- | --- | --- |
| Web Platform Tests (WPT) | https://github.com/web-platform-tests/wpt | Broad HTML/CSS/web-platform interoperability | Tracked, not yet automated in-repo | The renderer already exposes Ahem font loading and deterministic pixel-diff APIs, but the repository does not yet include checked-in WPT fixtures, reference renderings, or a runner. |
| WPT live results | https://wpt.fyi/ | Published interoperability results | Referenced only | Useful as an external comparison target once Broiler.HTML starts publishing suite results. |
| CSS 2.1 test suite | https://test.csswg.org/suites/css2.1/20110323/html4/ | CSS 2.1 rendering conformance | Skipped in current repo snapshot | No checked-in harness or baseline-image corpus exists yet. |
| Acid3 | http://acid3.acidtests.org/ | Historical HTML/CSS/DOM renderer milestone test | Tracked manually | Source comments reference Acid3-related work, but the repository does not currently contain the corresponding test assets or automated checks. |
| html5lib tests | https://github.com/html5lib/html5lib-tests | HTML tokenizer/tree-construction parsing compliance | Skipped in current repo snapshot | The repository does not currently include an adapter that imports the html5lib corpus into automated parser tests. |

## Repository-supported compliance workflow

Until a dedicated compliance harness is checked in, the repeatable in-repo workflow is:

1. Build the solution.
2. Load deterministic fixture fonts when needed with `Broiler.HTML.Image.HtmlRender.LoadFontFromFile(...)`.
3. Render the fixture with `Broiler.HTML.Image.HtmlRender`.
4. Compare the output against a baseline image with `PixelDiffRunner.Compare(...)`.
5. Use `MismatchClassifier.Classify(...)` to bucket failures and track regressions.

## Status publication

This document is the repository's source of truth for public-suite tracking:

- which public suites are tracked
- whether each suite is automated, manual, or skipped
- why a suite is skipped when it is not yet integrated

As checked-in suite fixtures and runners are added, this table should be updated from `Tracked` / `Skipped` to `Passing`, `Failing`, or `Partially passing` with links to the relevant test assets and results.
