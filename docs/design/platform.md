# Platform, dependencies and packaging

## Target framework

`net10.0` only. A lower target would need shims for APIs Core uses, and .NET 8 leaves support soon after PaperDoll's first release. This can be revisited if a consumer needs it.

## Core is fully managed and AOT-safe

`PaperDoll.Core` has no native dependencies, uses no reflection, and builds with the trimming and AOT analysers on and warnings as errors. Its one dependency is StbImageSharp, a managed PNG and TGA decoder; .NET has no cross-platform image decoder in the box. The demo publishes and runs under NativeAOT.

Snapshots and the CLI write JSON by hand rather than through reflection-based serialisation, for the same reason.

## Renderers do not carry their framework

- `PaperDoll.FNA` is compiled against FNA but has no package dependency on it. FNA is not on NuGet and recommends being referenced from source; the game brings FNA and its native libraries.
- `PaperDoll.MonoGame` is compiled against MonoGame's DesktopGL package with no package dependency. The game references the platform package it wants (DesktopGL or WindowsDX) and uses the HiDef profile. Both packages ship an assembly with the same name and API, so one build serves both. Other MonoGame backends throw `NotSupportedException`.

The library projects use neither ImGui nor any UI toolkit; only the demos do.

## Shaders are compiled once and committed

One HLSL source, `src/PaperDoll.FNA/Shaders/PaperDoll.fx`, is compiled to FNA's effect format (`fx_2_0`, with fxc) and to MonoGame's, one effect per backend (with `dotnet-mgfxc`, a local tool pinned in `dotnet-tools.json`). The compiled effects are committed and embedded, so building PaperDoll needs no shader compiler on any system. Each build script records the hash of the source it compiled; a test fails if the shader changes without both being rebuilt.

## FNA in the repository

FNA's managed source is kept unmodified in `lib/FNA` with its licences, and the demo's Windows x64 native libraries (and ImGui.NET with cimgui) in `lib/`, so a clean clone builds with nothing but NuGet. Updating FNA means replacing `lib/FNA` with the same file list from a newer release. `FNA.Settings.props` at the root is FNA's own override hook.

## Outside code

Every outside package is pinned in `Directory.Packages.props` (central package management). New dependencies are added sparingly, with their licence checked first; licence texts for bundled code sit beside it in `lib/` and are listed in `LICENSE.md`.

## Licence and attribution

BSD 2-Clause, the same as all three upstreams. A port by behaviour is still derived work, so their notices are kept: `LICENSE.md` holds PaperDoll's notice followed by the upstream notices, and [attribution.md](../attribution.md) names the projects PaperDoll is ported from and the outside code it uses. Both files ship inside every package.

## Packages

`PaperDoll.Core`, `PaperDoll.FNA` and `PaperDoll.MonoGame`, each with `LICENSE.md`, `attribution.md`, the logo and a README from `docs/packages/`. The snapshot library, the CLI and the demos are never packaged.
