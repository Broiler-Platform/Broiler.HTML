# HTML 5.2 Plain-HTML Test Suite

This directory contains the repository-owned HTML 5.2 test suite for
Broiler.HTML. The suite is designed for a static HTML renderer and does not use
WPT fixtures or JavaScript execution.

It is part of the unstable, AI-assisted Broiler.HTML preview and is covered by the
repository [human review](../../HUMAN_REVIEW.md), [Apache-2.0 license](../../LICENSE),
and [HTML Renderer provenance notice](../../THIRD_PARTY_NOTICES.md). Test results do not
by themselves constitute a safety approval.

The detailed design lives in
[`docs/html52-test-suite-plan.md`](../../docs/html52-test-suite-plan.md).
The planned CSS modules expansion lives in
[`docs/css-modules-test-suite-plan.md`](../../docs/css-modules-test-suite-plan.md).
Current coverage status is generated in
[`docs/compliance.md`](../../docs/compliance.md). Contribution rules live in
[`CONTRIBUTING.md`](CONTRIBUTING.md).

## Layout

```text
tests/html52/
  manifest.schema.json       JSON Schema for suite manifests.
  manifest.json              Root hand-authored suite manifest.
  coverage/                  Coverage maps for spec areas and planned work.
  assets/                    Local fonts, images, SVG, CSS, and XML fixtures.
  cases/                     Hand-authored input documents.
  references/                Checked-in expected outputs.
  generated/                 Reproducible generated cases and expectations.
```

The suite now includes tokenizer, DOM/tree-construction, visual, resource,
stress, and generated CSS module parser smoke cases. The root `manifest.json`
loads `generated/css-modules/manifest.generated.json` through
`generatedManifests`, so the CSS module suite is part of the comprehensive
HTML 5.2 run rather than a separate test tree. CSS module expansion is tracked
through the generated registry and coverage map under `generated/css-modules/`
and `coverage/css-modules.json`.

## Commands

Validate the manifest:

```bash
npm run html52:validate
```

List selected cases without executing them:

```bash
npm run html52:run -- --dry-run
```

Check that the generated CSS module suite is merged into the comprehensive
HTML 5.2 run:

```bash
npm run html52:integrated:check
```

Run the parser-only suite:

```bash
npm run html52:parser
```

Run the Phase 2 visual smoke suite:

```bash
npm run html52:visual
```

Run the Phase 3 tables/forms/widgets suite:

```bash
npm run html52:phase3
```

Run the Phase 4 resources/replaced-content suite:

```bash
npm run html52:phase4
```

Run the Phase 5 CSS/text/painting suite:

```bash
npm run html52:phase5
```

Run the CSS-focused smoke subset, including active CSS module parser sweeps:

```bash
npm run html52:css
```

Run only the Phase 2 stable-core CSS module parser sweeps:

```bash
npm run html52:css-stable
```

Run only the Phase 3 renderer-breadth CSS module parser sweeps:

```bash
npm run html52:css-breadth
```

Run only the Phase 4 draft and early-draft CSS module parser sweeps:

```bash
npm run html52:css-drafts
```

Regenerate and run the Phase 5 CSS module stress/fuzz fixtures:

```bash
npm run html52:css-generate-phase5
npm run html52:css-stress
```

Run the Phase 6 XHTML/legacy/stress/security suite:

```bash
npm run html52:phase6
```

Run the pull-request smoke subset:

```bash
npm run html52:smoke
```

Run the full suite:

```bash
npm run html52:run
```

Run the comprehensive merged HTML/CSS suite:

```bash
npm run html52:comprehensive
```

Run the full suite twice to check deterministic repeatability:

```bash
npm run html52:repeat
```

Summarize coverage and report planned-but-uncovered areas:

```bash
npm run html52:coverage
```

Refresh or check the generated CSS module registry and initial coverage map:

```bash
npm run html52:css-registry
npm run html52:css-registry:check
npm run html52:css-coverage
npm run html52:css-coverage:check
```

Check whether in-scope CSS modules have reached their target implementation
oracle depth. This is an opt-in completion gate and is expected to fail until
deeper computed-style, layout, display-list, and render coverage lands:

```bash
npm run html52:css-implementation:check
```

Refresh or check the generated compliance dashboard:

```bash
npm run html52:dashboard
npm run html52:dashboard:check
```

All suite resources must remain local to this directory. Tests may contain
`script` markup only when the assertion is about static parsing, fallback, or
inert rendering behavior.
