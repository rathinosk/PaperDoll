# PaperDoll

<img src="assets/paperdoll-logo.png" alt="PaperDoll logo: three paper dolls holding hands" width="128">

A 2D puppet runtime for .NET, [FNA](https://fna-xna.github.io/) and [MonoGame](https://monogame.net/). PaperDoll loads rigged cutout puppets in the Inochi2D format (`.inp`, `.inx`), evaluates their parameters, deforms their meshes, runs their physics and animations, and draws them through FNA or MonoGame. Several puppets can be on screen at once, each with its own state.

It is an open-source C# port, by behaviour, of [Inochi2D](https://github.com/Inochi2D/inochi2d) and [nijilive](https://github.com/nijigenerate/nijilive), with [nicxlive](https://github.com/nijigenerate/nicxlive) as a secondary reference. The originals are written in D and C++; PaperDoll follows their behaviour and structure but is written anew in C#, with no code copied from them. It reads puppets made with Inochi Creator, nijigenerate and Mannequin Character Generator; it does not edit or write them.

## Features

- **The INP1 format** (`.inp`, `.inx`) from Inochi Creator 0.7 and 0.8, nijigenerate and Mannequin Character Generator; PNG and TGA textures
- **Parameters**: 1D and 2D, all five merge modes, Nearest, Linear and Cubic keyforms, binding transforms, z-sort, opacity, tints and mesh deforms
- **MeshGroup deformation**: a deformed mesh bends the Parts and nodes below it, triangle by triangle
- **Masks and blend modes**: `Mask` and `DodgeMask`, Mask nodes, all nineteen blend modes (the advanced ones with their real equations), and Composites drawn as layers
- **Physics**: SimplePhysics pendulums and spring pendulums, stepped per instance; optionally swinging when the game moves the puppet
- **Animations**: looping, lead-in and lead-out, every lane interpolation, several at once, without overwriting the values the game sets
- **Many puppets at once**: one loaded puppet shared by any number of instances, each with its own time step, parameters, physics and animations
- **Fast and lean**: no allocations per frame once warmed up; textures cropped and packed into atlases, so many parts share a draw call; optional half-size textures
- **Engine-free core**: `PaperDoll.Core` is fully managed, reflection-free, and trimming- and NativeAOT-safe, with draw lists any renderer can use

[docs/features.md](docs/features.md) has the full list, including what is not supported.

## Quick look

```csharp
var puppet = Puppet.Load("character.inx");                   // immutable, shared by its instances
var instance = puppet.CreateInstance();                      // one per copy on screen
instance.PlayAnimation("Blink", loop: true);
instance.SetParameterValue(puppet.FindParameter("Head:: Yaw-Pitch")!, new(0.5f, 0));
instance.Update(deltaSeconds);                               // parameters, animations, physics, deformation
renderer.Draw(instance, textures, placement);                // PaperDoll.FNA or PaperDoll.MonoGame
```

## Documentation

- [docs/getting-started.md](docs/getting-started.md): from a puppet file to the screen, with parameters, animations, physics and drawing
- [docs/api.md](docs/api.md): the public types, their lifetimes, threading, and the order a game calls them in
- [docs/features.md](docs/features.md): what PaperDoll supports of the format, and how each feature behaves
- [docs/faq.md](docs/faq.md): common questions, from which files load to placing puppets and swinging physics
- [docs/design/](docs/design/README.md): why PaperDoll is built the way it is
- [docs/attribution.md](docs/attribution.md): the projects PaperDoll is ported from, and the outside code it uses

## NuGet Packages

| Package | Contents |
| --- | --- |
| [`PaperDoll.Core`](https://www.nuget.org/packages/PaperDoll.Core) | Engine-free and fully managed: the format parser, node tree, parameters, deformers, physics, animations and draw order. Its output per drawable is plain arrays (positions, UVs, indices, opacity, blend mode, masks, draw order) |
| [`PaperDoll.FNA`](https://www.nuget.org/packages/PaperDoll.FNA) | A thin renderer that draws Core's output through FNA, with masks, blend modes and texture atlases. Built against FNA 26.10; the game brings FNA itself |
| [`PaperDoll.MonoGame`](https://www.nuget.org/packages/PaperDoll.MonoGame) | The same renderer for MonoGame 3.8.6 (DesktopGL or WindowsDX). Built against MonoGame; the game brings its MonoGame platform package |

## Demo and tools

The repository has a demo that shows up to five puppets at once, each placed and scaled on its own, with an ImGui panel of parameter sliders, physics and animation controls, a texture browser and a blend-mode test scene. It runs on FNA (`samples/PaperDoll.Demo`) or MonoGame (`samples/PaperDoll.Demo.MonoGame`, which opens puppet files dropped on its window), on Windows x64 for now:

```sh
dotnet run --project samples/PaperDoll.Demo -- [puppet files...]
dotnet run --project samples/PaperDoll.Demo -- [puppet files...] --play Blink --set "Head:: Yaw-Pitch=0.5,0"
dotnet run --project samples/PaperDoll.Demo -- --test-scene                          # every blend mode
dotnet run --project samples/PaperDoll.Demo -c Release -- [puppet files...] --bench   # frame time, allocations, draw calls, GPU memory
dotnet run --project samples/PaperDoll.Demo.MonoGame -- [puppet files...]             # MonoGame DesktopGL
dotnet run --project samples/PaperDoll.Demo.MonoGame -p:MonoGameBackend=WindowsDX -- [puppet files...]
```

Other demo options: `--screenshot <png>` (with `--frames <n>`), `--no-physics`, `--no-atlas` and `--texture-scale <1|0.5|0.25|0.125>`.

Each release on GitHub has the demo as a ready-to-run download (`PaperDoll-Demo-<version>-win-x64.zip`: both demos, binaries only), and the `paperdoll` tool for Windows, Linux and macOS. Puppets to try: Inochi2D's example models, Aka and Midori, at <https://github.com/Inochi2D/example-models>.

The `paperdoll` command-line tool (`src/PaperDoll.Tools`) prints a puppet's tree, parameters, animations and textures (`inspect`), reports what in a puppet PaperDoll skips or ignores (`validate`), and replays regression snapshots saved from the demo (`snapshots`):

```sh
dotnet run --project src/PaperDoll.Tools -- inspect character.inx --section params
dotnet run --project src/PaperDoll.Tools -- validate puppets/
```

## Building from source

```sh
dotnet build PaperDoll.slnx
dotnet test --solution PaperDoll.slnx
dotnet pack src/PaperDoll.Core -c Release    # and src/PaperDoll.FNA, src/PaperDoll.MonoGame
```

A clean clone builds with no other downloads besides NuGet: FNA's source, its Windows x64 native libraries and ImGui.NET are in `lib/`, and the compiled shaders are committed (rebuilding them after editing `PaperDoll.fx` needs fxc and mgfxc; see the scripts in each renderer's `Shaders/` folder). Sample puppets are not in the repository; tests that need them skip unless a `.samples/` folder (or `PAPERDOLL_SAMPLES`) points at some.

## Requirements

- .NET 10 (`net10.0`)
- FNA 26.10, for `PaperDoll.FNA` and its demo (the repository carries it in `lib/`, with FNA's Windows x64 native libraries)
- MonoGame 3.8.6 with the HiDef profile, DesktopGL or WindowsDX, for `PaperDoll.MonoGame` and its demo (from NuGet)

## Licence

BSD 2-Clause, the same as the projects it ports. See [LICENSE.md](LICENSE.md) for PaperDoll's notice and the upstream notices.

PaperDoll is made by Rathinosk: <https://rathinosk.com>.
