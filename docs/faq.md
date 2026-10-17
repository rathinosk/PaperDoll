# Frequently asked questions

## About PaperDoll

### What is PaperDoll for?

Showing rigged 2D characters in a .NET game: visual novels, dialogue portraits, characters on a stage. You load a puppet made in an Inochi2D editor, set its parameters (head turn, mouth, eyes) and play its animations from your game code, and PaperDoll deforms and draws it.

### Is it an official Inochi2D project?

No. PaperDoll is an independent port, by behaviour, of the Inochi2D and nijilive runtimes to C#. It reads the same files, but it is not affiliated with or endorsed by either project. [attribution.md](attribution.md) records what it is ported from.

### What does it cost, and what is the licence?

Nothing. PaperDoll is BSD 2-Clause, the same as the projects it ports: you can use it in commercial and closed-source games. Keep the notices in `LICENSE.md` with your game (the packages carry it).

The puppets are another matter: each is its author's work, under whatever terms the author set. `Puppet.Meta` shows the rights stored in the file (`Rights`, `Copyright`, `LicenseUrl`); PaperDoll reports them and enforces nothing.

## Puppets and editors

### Which files can it load?

INP1 puppets (`.inp` and `.inx`) from Inochi Creator 0.7 and 0.8, nijigenerate and Mannequin Character Generator. [features.md](features.md) lists every node type and feature, supported or not.

### Can it load Inochi Creator 0.9 (INP2) files?

Not yet. INP2 is a different format, and `Puppet.Load` throws a `PuppetFormatException` that says so. Save or export from an editor that writes INP1.

### Can I load a PSD, or a Live2D model?

No. PaperDoll reads finished Inochi2D puppets only. Rig your character in Inochi Creator or nijigenerate (both import PSDs), and load the saved puppet. Live2D models use a different, proprietary format.

### Can PaperDoll edit or save puppets?

No. It reads puppets and leaves authoring to the editors that made them.

### My puppet looks different from the editor. What should I check?

1. Run `paperdoll validate <file>` (the CLI in `src/PaperDoll.Tools`), or log `Puppet.Issues`. It lists everything in the file that PaperDoll skips or ignores, such as unsupported node types, which are left out with their children.
2. Check [nijilive-differences.md](design/nijilive-differences.md): a few behaviours differ from nijilive on purpose.
3. Open the puppet in the demo (`samples/PaperDoll.Demo`) and move its parameters there, to tell a puppet problem from a problem in your own code.

If it still looks wrong, open an issue with the file (or a minimal one that shows the problem) and a screenshot from the editor.

### Should I ship `.inx` or `.inp`?

Either loads. `.inx` is what the editors save, with editor-only data (which PaperDoll ignores); `.inp` is the exported file. Inochi Creator's export can bake MeshGroup deformation into the nodes below, prune nodes and repack textures, so the same puppet can differ slightly between the two: test the one you ship.

## Using it in a game

### Which engines does it work with?

FNA and MonoGame (DesktopGL and WindowsDX), through `PaperDoll.FNA` and `PaperDoll.MonoGame`. `PaperDoll.Core` has no engine at all: its draw list is plain arrays, so a renderer for another engine can be written against it ([api.md](api.md#the-draw-list-for-custom-renderers)).

### Which platforms?

`PaperDoll.Core` is fully managed .NET 10 code, AOT- and trim-safe, so it runs wherever .NET 10 does. The renderers run wherever your FNA or MonoGame build runs; PaperDoll has so far been tested on Windows x64.

### Why .NET 10 only?

Core relies on APIs that older targets would need shims for, and .NET 8 leaves support soon after PaperDoll's first release. See [design/platform.md](design/platform.md).

### Does it do face tracking or lip sync?

Not by itself. PaperDoll has no input of its own: your game sets parameter values. Feeding them from face tracking, an audio level or a dialogue script is up to you, and any source that gives numbers works.

### Does it support animations?

Yes, the animations stored in the puppet, with looping, lead-in and lead-out, and several at once (`instance.PlayAnimation("Blink", loop: true)`). They act on a copy of your parameter values, so stopping an animation hands the puppet back to whatever your game set.

### How do I find a puppet's parameter and animation names?

`paperdoll inspect <file> --section params` lists the parameters, and `--section anims` the animations; without `--section`, `inspect` prints everything (metadata, tree, parameters, animations, textures, extensions and load issues). In code, `puppet.Parameters` and `puppet.Animations`. The demo shows them all with sliders.

### Can I show the same character twice?

Yes. Load it once and call `CreateInstance()` for each copy; each instance has its own parameters, animations and physics, and they share the puppet's data and textures.

### How do I place, scale or flip a puppet?

With the `transform` you pass to `PuppetRenderer.Draw`: any XNA `Matrix` from puppet space to screen pixels. A negative X scale mirrors the puppet. `instance.Bounds` right after creation gives its size at rest, to centre it or stand it on the floor.

### Why does the hair swing when the puppet turns, but not when I move it?

Physics reacts to what moves the puppet's nodes. Moving the puppet across the screen happens in your transform, which physics does not see. Set `instance.ScenePosition` each frame to where you placed it (your position divided by your scale), and call `ResetPhysics()` after a jump so it does not swing wildly.

### `Draw` throws about render target contents. Why?

The renderer draws each puppet in an offscreen layer and then switches back to your target, so that target must keep what is already drawn. Set `RenderTargetUsage.PreserveContents` on the back buffer (in `PreparingDeviceSettings`) or on your `RenderTarget2D`. [getting-started.md](getting-started.md#a-puppet-on-screen) shows how.

### Why do textures fail to load on MonoGame?

Set `GraphicsProfile = GraphicsProfile.HiDef` on the `GraphicsDeviceManager`. MonoGame defaults to Reach, which cannot hold the 4096-pixel atlas pages.

### Why do my sprites draw wrong after a puppet?

`Draw` leaves the blend, depth-stencil, rasterizer and sampler states changed, as `SpriteBatch` does. Set the states you need again (`SpriteBatch.Begin` with explicit states does this).

### Can I update puppets on another thread?

Yes, with one thread per instance at a time: a `PuppetInstance` is not thread-safe, but separate instances (even of the same puppet) can be updated in parallel, and a `Puppet` can be read from anywhere. Drawing stays on the graphics thread. Loading can happen on a background thread.

### How fast is it? Will it run on a weak machine?

Updating and drawing allocate nothing per frame once warmed up, so there are no garbage-collection pauses. Drawing cost follows the puppet's size on screen. For low memory, `new PuppetTextures(device, puppet, textureScale: 0.5f)` uploads textures at half size, which looks the same for puppets drawn at half size or less. The demo's `--bench` option measures your own puppets on your own machine; [getting-started.md](getting-started.md#performance) has reference figures.

### Can I draw only some parts of a puppet?

`PuppetRenderer.Draw` takes an optional `filter` that decides which Parts are drawn. For permanent changes (an outfit toggle), use the puppet's own parameters if it has them, since hiding Parts by filter skips them but keeps everything else as rigged.
