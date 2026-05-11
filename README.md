# Broiler.HTML

Broiler.HTML is a modular .NET HTML renderer split into focused assemblies for parsing, CSS processing, layout, painting, image generation, and WPF hosting.

## Repository goals

- keep the renderer modular and maintainable
- document the public API and architecture of the current Broiler.HTML codebase
- track public compliance suites and their status from inside the repository
- make baseline build and compliance checks repeatable

## Solution layout

The solution lives under `/home/runner/work/Broiler.HTML/Broiler.HTML/Source/Broiler.HTML.slnx` and is organized into these main assemblies:

- `Broiler.HTML.Primitives` - shared primitive types
- `Broiler.HTML.Utils` - common utilities and resource helpers
- `Broiler.HTML.Adapters` - backend-neutral adapter abstractions
- `Broiler.HTML.Core` - core entities and deterministic IR helpers
- `Broiler.HTML.CSS` - CSS parsing and stylesheet handling
- `Broiler.HTML.Dom` - DOM/layout processing
- `Broiler.HTML.Orchestration` - HTML parsing and renderer orchestration
- `Broiler.HTML.Rendering` - paint-time handlers and rendering logic
- `Broiler.HTML.Image` / `Broiler.HTML.Image.Compat` - image rendering, deterministic comparison, and Skia compatibility
- `Broiler.HTML.WPF` - WPF rendering surface and controls
- `Broiler.HTML` - shared public surface used by platform adapters

## Public API highlights

- `Broiler.HTML.Image.HtmlRender` renders HTML to in-memory bitmaps, PNG bytes, and files.
- `Broiler.HTML.Image.PixelDiffRunner` compares rendered output to a baseline image.
- `Broiler.HTML.Image.MismatchClassifier` classifies visual mismatches for compliance triage.
- `Broiler.HTML.WPF.HtmlRender` renders HTML into WPF drawing contexts and images.
- `Broiler.HTML.WPF.HtmlPanel` and `Broiler.HTML.WPF.HtmlLabel` expose WPF controls.
- `Broiler.HTML.Adapters.RAdapter` is the backend extension point for graphics, fonts, images, clipboard, and context-menu services.

## Build and validation

Run the repository build from the solution directory:

```bash
cd /home/runner/work/Broiler.HTML/Broiler.HTML/Source
dotnet build Broiler.HTML.slnx
dotnet test Broiler.HTML.slnx
```

`Broiler.HTML.WPF` now enables Windows targeting so solution-wide restore/build validation also works on non-Windows CI hosts.
The current solution does not contain checked-in test projects yet, so `dotnet test` currently acts as a repository validation command rather than a substantive automated renderer test suite.

## Documentation

- [Architecture and API notes](docs/architecture.md)
- [Compliance suites and status tracking](docs/compliance.md)

## Compliance status

Broiler.HTML already contains deterministic render and pixel-diff primitives that can be used to benchmark output against public suites. The tracked suites, current status, and explicit skip reasons are documented in [docs/compliance.md](docs/compliance.md).
