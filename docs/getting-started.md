# Getting started

PaperDoll loads Inochi2D puppets (`.inp`, `.inx`, the INP1 format), poses and animates them, and draws them through FNA or MonoGame. This page takes a puppet from a file to the screen. [features.md](features.md) lists what PaperDoll supports, [api.md](api.md) maps the public types, and [faq.md](faq.md) answers common questions.

## Packages

| Package | Use it for |
| --- | --- |
| `PaperDoll.Core` | Loading puppets, setting parameters, playing animations, physics, and the draw list each instance produces. Fully managed; no engine, no native code |
| `PaperDoll.FNA` | Drawing those draw lists through FNA, with masks, blend modes and Composite layers |
| `PaperDoll.MonoGame` | The same, through MonoGame (DesktopGL or WindowsDX) |

All three target .NET 10. `PaperDoll.FNA` is built against FNA 26.10 but does not bring FNA with it: FNA is not on NuGet, so reference it from source in your game, as FNA recommends, and ship its native libraries (fnalibs) next to your executable. `PaperDoll.MonoGame` is built against MonoGame 3.8.6 and does not bring MonoGame either: reference the platform package your game uses, `MonoGame.Framework.DesktopGL` or `MonoGame.Framework.WindowsDX`.

The two renderers are one source with the same types; the example below uses FNA. On MonoGame, write `using PaperDoll.MonoGame;` in place of `using PaperDoll.Fna;` and set `GraphicsProfile = GraphicsProfile.HiDef` on the `GraphicsDeviceManager` (MonoGame defaults to Reach, which cannot hold the 4096-pixel atlas pages).

## The model

| Type | What it is | How many |
| --- | --- | --- |
| `Puppet` | A loaded file: node tree, meshes, textures, parameters, animations. Immutable | One per file |
| `PuppetInstance` | One copy of a puppet on screen: its parameter values, animations, physics and draw list | One per copy on screen; the same puppet can be on screen twice |
| `PuppetTextures` | A puppet's textures on the GPU, packed into atlas pages | One per loaded puppet, shared by its instances |
| `PuppetRenderer` | Draws instances | One for the whole game |

There is no global clock or camera: each instance is updated with its own time step, and each draw call takes its own transform.

## A puppet on screen

```csharp
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PaperDoll;
using PaperDoll.Fna;

public sealed class SceneGame : Game
{
    private readonly GraphicsDeviceManager _graphics;
    private readonly string _path;
    private Puppet _puppet = null!;
    private PuppetTextures _textures = null!;
    private PuppetRenderer _renderer = null!;
    private PuppetInstance _left = null!, _right = null!;
    private PuppetBounds _rest;
    private Parameter? _headYaw;

    public SceneGame(string path)
    {
        _path = path;
        _graphics = new GraphicsDeviceManager(this);
        // PuppetRenderer draws through offscreen layers: the back buffer must keep what is already drawn.
        _graphics.PreparingDeviceSettings += (_, e) =>
            e.GraphicsDeviceInformation.PresentationParameters.RenderTargetUsage = RenderTargetUsage.PreserveContents;
    }

    protected override void LoadContent()
    {
        _puppet = Puppet.Load(_path);                              // immutable, shared
        _textures = new PuppetTextures(GraphicsDevice, _puppet);   // one per loaded puppet
        _textures.LoadAll();                                       // upload now, not on the first frame
        _renderer = new PuppetRenderer(GraphicsDevice);            // one for everything
        _left = _puppet.CreateInstance();                          // each on-screen copy has its own state
        _right = _puppet.CreateInstance();
        _rest = _left.Bounds;                                      // the puppet's size at rest, for placing it
        _headYaw = _puppet.FindParameter("Head:: Yaw-Pitch");
        if (_right.TryGetAnimation("Blink", out var blink))
            blink.Play(loop: true);
    }

    protected override void Update(GameTime gameTime)
    {
        var seconds = (float)gameTime.TotalGameTime.TotalSeconds;
        if (_headYaw is not null)
            _left.SetParameterValue(_headYaw, new System.Numerics.Vector2(MathF.Sin(seconds), 0));

        var delta = (float)gameTime.ElapsedGameTime.TotalSeconds;
        _left.Update(delta);
        _right.Update(delta);
        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(Color.CornflowerBlue);
        _renderer.Draw(_left, _textures, Place(0.3f, 400));
        _renderer.Draw(_right, _textures, Place(0.3f, 900));
        base.Draw(gameTime);
    }

    /// <summary>Puppet space to screen pixels: centred on <paramref name="x"/>, feet near the bottom of the window.</summary>
    private Matrix Place(float scale, float x) =>
        Matrix.CreateTranslation(-_rest.Center.X, -_rest.Max.Y, 0)
        * Matrix.CreateScale(scale)
        * Matrix.CreateTranslation(x, GraphicsDevice.Viewport.Height - 20, 0);

    protected override void UnloadContent()
    {
        _renderer.Dispose();
        _textures.Dispose();
        base.UnloadContent();
    }
}
```

Parameter names belong to the puppet: list them with `paperdoll inspect <file> --section params` (the CLI in `src/PaperDoll.Tools`), or browse them in the demo.

## Loading

`Puppet.Load` reads a file path, a stream or bytes in memory, and throws `PuppetFormatException` if they are not a readable INP1 puppet (INP2 files from Inochi Creator 0.9 included). Anything a loaded puppet holds that PaperDoll skips or ignores (unsupported node types, editor keys, references to missing nodes) is listed in `Puppet.Issues` rather than thrown; `paperdoll validate <file>` prints the same list.

`new PuppetTextures(...)` uploads nothing until the first draw needs a texture. Call `LoadAll()` while loading (as in `LoadContent`) so the first frame does not stall decoding and uploading.

## Parameters

`SetParameterValue(parameter, value)` sets a parameter on one instance; the Y component is ignored for 1D parameters, and values outside the parameter's range act as the nearest end. The value takes effect at the next `Update`. `GetParameterValue` reads back what was set (or what physics last gave a parameter it drives), and `GetEvaluatedValue` what the puppet was last evaluated at, animations included. `ResetParameters` returns every parameter to its default.

Look parameters up once (`Puppet.FindParameter(name)` or `Puppet.Parameters`) and keep the `Parameter` objects: they belong to the loaded puppet and work with every instance of it.

## Animations

Each instance has one `AnimationPlayback` per animation in the puppet (`instance.Animations`, `GetAnimation(name)`, `TryGetAnimation`). `Play(loop, playLeadOut)`, `Pause`, `Stop(immediate)`, `Seek(frame)`, `Speed` and `Strength` control it, and several can run at once. `instance.PlayAnimation(name, loop)` and `StopAnimations()` are shortcuts. Animations act on a copy of your values: they never overwrite what you set, and when one stops your values apply again.

## Physics

Physics runs while `PhysicsEnabled` is on (the default) and steps by each instance's time step. Hair and cloth swing when parameters move the puppet. To have them swing when you move the puppet across the scene, set `ScenePosition` each frame to where you placed it, in puppet units (your screen position divided by your scale). After a jump (placing it somewhere new) call `ResetPhysics()`, or it swings as hard as a fast move would.

## Drawing

`PuppetRenderer.Draw(instance, textures, transform)` draws the instance as of its last `Update` into the current render target, within its viewport. `transform` maps puppet space (the puppet's own pixels, x right, y down) to viewport pixels: it is your camera and placement. Draw instances back to front.

The renderer draws each puppet through an offscreen layer, so the target you draw into must keep its contents: set `RenderTargetUsage.PreserveContents` on the back buffer (as above) or on your `RenderTarget2D`. `Draw` restores your render target and viewport afterwards and leaves the device's blend, depth-stencil, rasterizer and first two sampler states changed, as `SpriteBatch` does (on MonoGame's OpenGL backend, its vertex and index buffers too).

Each offscreen layer is drawn only over the pixels its puppet covers on screen, so drawing costs follow a puppet's size on screen, and a puppet entirely outside the viewport costs almost nothing. `Draw`'s optional `filter` draws only the Parts it accepts. `AdvancedBlending` (on by default) draws Multiply, Screen, Overlay and the other advanced modes with their real equations; off, they use cheaper fixed-function approximations, and some of them draw as Normal ([features.md](features.md#drawing) lists which). `LastDrawCalls` and `LastLayers` report what the last `Draw` cost.

Textures are premultiplied. If memory is tight, `new PuppetTextures(device, puppet, textureScale: 0.5f)` uploads them at half size (a quarter of the memory), which looks the same wherever the puppet is drawn at half its size or less.

## Without FNA or MonoGame

`PaperDoll.Core` does not need either. After `Update`, `instance.DrawList` holds what to draw, back to front: each Part's puppet-space positions, UVs, indices, texture slot, opacity, tints, blend mode and masks, and begin and end items around each Composite's layer; `instance.MaskSources` holds the shapes items are masked by. `Puppet.Textures[slot].Decode()` gives a texture's RGBA pixels (premultiplied). Any renderer can draw from these; `PaperDoll.FNA` and `PaperDoll.MonoGame` are two.

## Performance

Updating and drawing allocate nothing once warmed up. Measured on the development machine (Release, 1600x900, five puppets, physics on, every parameter moving): about 0.6 ms updating and 1.7 ms drawing for five Mannequin Character Generator puppets through FNA; drawing takes about 0.6 ms through MonoGame's DesktopGL and 0.4 ms through its WindowsDX. The demo's `--bench` option measures any set of puppets on your machine.
