# HTML 5.2 Suite Contributor Guide

This suite is repository-owned and intentionally plain HTML. Do not import WPT
fixtures, WPT references, or JavaScript-dependent tests into this directory.

## Adding A Case

1. Pick the coverage row first.

   Check `tests/html52/coverage/*.json` and choose the `featureId` the case is
   meant to cover. A new case should either close a coverage row, strengthen a
   weak row, or guard a regression.

2. Add a local fixture under `tests/html52/cases/<cluster>/`.

   Keep fixtures small and deterministic. All external resources must live under
   `tests/html52/assets/` or be data URIs. Network URLs are allowed only when the
   assertion is that the resource is classified as blocked and never fetched.

3. Choose the narrowest oracle that proves the assertion.

   Prefer `tokens` or `dom` JSON for parser behavior, `resourceLog` JSON for URL
   and fetch policy behavior, and `render` PNG only when the visual output is the
   behavior under test.

4. Generate the reference output with `Broiler.HTML.Tool`.

   Examples:

   ```bash
   dotnet run --project Source/Broiler.HTML.Tool/Broiler.HTML.Tool.csproj -- dump-dom --input tests/html52/cases/example.html --output tests/html52/references/dom/example.json
   dotnet run --project Source/Broiler.HTML.Tool/Broiler.HTML.Tool.csproj -- dump-resources --input tests/html52/cases/example.html --output tests/html52/references/resource-log/example.json --base-url file:///absolute/path/to/example.html
   dotnet run --project Source/Broiler.HTML.Tool/Broiler.HTML.Tool.csproj -- render --input tests/html52/cases/example.html --output tests/html52/references/images/example.png --width 800 --height 600 --disable-network
   ```

   Resource-log references should be generated with the same file base URL shape
   used by `scripts/html52/run-suite.mjs`.

5. Add one manifest entry in `tests/html52/manifest.json`.

   The entry must include `id`, `title`, `cluster`, `subcluster`, `featureId`,
   `spec`, `input`, `assertions`, `expectations`, `requires`, `scripts`, and
   `status`. Keep `assertions` specific enough that a reviewer knows what the
   reference is supposed to prove.

6. Verify locally.

   ```bash
   npm run html52:validate
   npm run html52:run -- --case <case-id> --skip-build
   npm run html52:coverage
   npm run html52:dashboard
   ```

## Baseline Review Workflow

Every render baseline update needs enough evidence for reviewers to understand
whether the pixel change is intended.

Include these artifacts or paths in the review:

- the changed fixture file
- the manifest entry or entries touched
- the old checked-in PNG baseline
- the new PNG baseline
- the `render.actual.png` produced by the runner
- the `render.diff.png` when the comparison fails
- the `render.report.json` mismatch report
- the feature id and spec section being asserted

Browser screenshots can seed a discussion, but they are not automatically
trusted references. When a browser is used as seed data, note the browser name
and version and review the output against the HTML 5.2 requirement before
checking in the baseline.

Do not refresh broad groups of PNGs mechanically. A baseline update should be
small, explainable, and tied to either a renderer fix, an intentional fixture
change, or a newly added case.

## Quarantine Policy

Use `status: "quarantined"` only for a useful case that should keep producing
artifacts but is not ready to block CI. Quarantine is temporary and must have an
owner and expiry.

Quarantined manifest entries must include:

```json
"status": "quarantined",
"quarantine": {
  "owner": "github-user-or-team",
  "expires": "2026-09-30",
  "reason": "Short explanation of the current failure.",
  "issue": "https://github.com/owner/repo/issues/123"
}
```

Validation fails when:

- a quarantined case has no `quarantine` block
- `owner`, `expires`, or `reason` is missing
- `expires` is not `YYYY-MM-DD`
- the expiry date has passed
- a non-quarantined case has a `quarantine` block

When a quarantine expires, either fix the case and return it to `active`, update
the expiry with a fresh reason, or remove the case if it is no longer valuable.

## CI Expectations

Pull requests run `npm run html52:smoke` plus manifest, coverage, and dashboard
checks. Scheduled CI runs the full suite. Before merging suite changes, run:

```bash
npm run html52:validate
npm run html52:smoke -- --skip-build
npm run html52:coverage -- --fail-on-uncovered
npm run html52:dashboard:check
```

Run `npm run html52:run -- --skip-build` when changing shared runner logic,
rendering behavior, or checked-in references.
