# Human review: Broiler.HTML

> **Status: APPROVED WITH CONDITIONS - first preview only.**

This record captures the first human preview review for Broiler.HTML. The approval is
intentionally narrow: the component is acceptable for first-preview publication and
exploratory integration, but it is not a production security approval and not a claim
that the renderer is free of defects or vulnerabilities.

"Safe" is not an absolute guarantee. Approval means only that the named reviewer found
the specified revision reasonably suitable for the stated preview use, subject to the
recorded limitations and the software license's warranty disclaimer.

## Review target

- **Component:** Broiler.HTML
- **Scope:** The HTML/CSS renderer, layout and painting pipeline, image and WPF
  adapters, command-line renderer, WPT tooling, and transitional orchestration code
  still present in Broiler.HTML.
- **Release:** First preview
- **Commit:** `a9915ed6e59aeb7fb1ffa564ff97e5b6e5e657dc`
- **Reviewer:** MaiRat / Maik Ratzmer
- **Reviewer contact or profile:** MaiRat
- **Review date:** 2026-07-01
- **Intended preview use:** First-preview evaluation of the static HTML renderer,
  refactor direction, and local rendering/compliance tooling. Use is limited to
  controlled development or test environments and non-production preview consumers.

Any source change after the reviewed commit invalidates this approval until the changed
revision is reviewed again. This record does not cover uncommitted local source changes
that may be present in a working tree after the reviewed commit.

## Summary decision

Broiler.HTML is acceptable as a first preview under the conditions below.

The component is still in an active refactor from the original HTML renderer. Much of
the current Broiler.HTML code is obsolete or deprecated compatibility surface and
transitional rendering logic. Ongoing work is expected to move responsibilities into
CSS, Layout, DOM, and other focused components. If Broiler.HTML remains as a long-lived
component, its remaining role should primarily be orchestration.

Security-sensitive code may still be present because the component continues to contain
renderer paths that are still partially used. The preview must therefore carry explicit
safety warnings and must not be treated as a hardened renderer for hostile HTML/CSS.

## Required evidence

The reviewer records links, logs, or concise findings for every item:

- [x] Build and automated test commands were run or reviewed; results are recorded
      below.
- [x] Security-sensitive inputs, trust boundaries, file/network access, native interop,
      and code-execution paths were considered at preview scope; no full security
      clearance is claimed.
- [x] Dependency and license notices were checked at preview scope, including inherited
      upstream code.
- [x] AI-generated or AI-modified code received source-level review at preview scope;
      no AI summary is accepted as a substitute for human review.
- [x] Public APIs, failure behavior, known limitations, and preview compatibility risks
      were assessed.
- [x] Static analysis, dependency/vulnerability scanning, or an explicit reason for
      omitting each was recorded.
- [x] Open findings and residual risks are listed below.

### Evidence and commands

- `git rev-parse HEAD`: `a9915ed6e59aeb7fb1ffa564ff97e5b6e5e657dc`.
- `dotnet build Source/Broiler.HTML.Graphics.Win32.Demo/Broiler.HTML.Graphics.Win32.Demo.csproj`:
  completed with exit code 0 on 2026-07-01 in the current local working tree. The
  targeted demo build reported one `CS0618` warning for the obsolete compatibility
  API `HtmlContainer.SetHtml(string, CssData?, string?)`.
- `dotnet build Source/Broiler.HTML.slnx`: re-run on 2026-07-01 in the current local
  working tree and completed with exit code 0, 0 warnings, and 0 errors. The earlier
  Win32 demo `CS0534` failure was not reproduced.
- `dotnet test Source/Broiler.HTML.slnx`: completed with exit code 0 on 2026-07-01.
  The current solution output does not show substantive .NET test execution, so this is
  treated as repository build/restore validation rather than renderer coverage.
- `npm test`: passed on 2026-07-01. Node test runner reported 31 tests, 31 passing,
  0 failing.
- Obsolete/deprecated compatibility APIs were confirmed in source, including `CssData`,
  legacy `HtmlRender` overloads, legacy `HtmlContainer` APIs, and explicit `CS0618`
  suppressions where transitional paths are still bridged.
- Dedicated static analysis and dependency/vulnerability scanning were not run for this
  first-preview review. They remain required before any production or security-sensitive
  approval.

### Findings and residual risks

- **High:** Security-sensitive renderer behavior remains in scope. Rendering untrusted
  HTML/CSS can involve parsing, resource resolution, fonts, images, file/network access
  through adapters, and native/WPF/Direct2D surfaces. Mitigation: preview warning,
  controlled inputs, sandboxing, and no production or hostile-document use.
- **Medium:** Significant obsolete/deprecated code remains. Compatibility APIs such as
  `CssData` and legacy `SetHtml`/`Render` overloads are still present while behavior is
  moved toward CSS, Layout, DOM, and orchestration boundaries. Mitigation: keep
  deprecation warnings visible, avoid promoting legacy APIs, and continue the refactor.
- **Medium:** Automated renderer conformance and security coverage remains incomplete.
  The checked NPM tests cover WPT tooling, while `dotnet test` currently provides little
  evidence of renderer behavior. Mitigation: expand .NET renderer tests, WPT samples,
  fuzz/stress cases, and security boundary tests.
- **Low:** Preview APIs, rendering behavior, and platform support remain unstable.
  Consumers should expect breaking changes.

## Decision

Select exactly one:

- [ ] **APPROVED FOR PREVIEW** within the intended-use scope above.
- [x] **APPROVED WITH CONDITIONS** listed below.
- [ ] **NOT APPROVED** for preview use.

**Conditions:**

1. Approval is limited to the first preview and controlled development/test use.
2. Public preview documentation and release notes must include explicit safety warnings:
   Broiler.HTML is not hardened, is not approved for hostile untrusted HTML/CSS, and may
   still contain security-sensitive rendering paths.
3. Preview consumers must use controlled inputs, sandboxing, and restricted resource
   loading where untrusted content cannot be avoided.
4. Obsolete/deprecated APIs are compatibility surfaces only and should not be promoted as
   the preferred long-term API.
5. Dedicated security review, static analysis, and dependency/vulnerability scanning are
   required before any production, hostile-input, or security-sensitive approval.
6. Any source change after the reviewed commit requires renewed review before this
   approval can be applied to the changed revision.

## Human attestation

I confirm that I am a human developer, that I personally reviewed the revision and
evidence identified above, and that the decision is my own. I understand that this
attestation is a scoped engineering review, not a warranty or a claim that the component
is free of defects or vulnerabilities.

- **Name:** MaiRat / Maik Ratzmer
- **Signature or attributable commit:** MaiRat / Maik Ratzmer; the release commit that
  carries this review record should provide repository attribution.
- **Date:** 2026-07-01

AI tools may help assemble evidence, but the named human reviewer remains responsible
for the decision, attestation, and release attribution.
