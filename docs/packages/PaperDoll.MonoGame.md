# PaperDoll.MonoGame

Draws PaperDoll puppets through [MonoGame](https://monogame.net/) 3.8.6, on its DesktopGL (OpenGL) and WindowsDX (DirectX 11) backends: Parts, Composite layers, masks and all nineteen blend modes of the format, with each puppet's textures cropped and packed into atlas pages so many parts share a draw call. Allocation-free per frame once warmed up. It is `PaperDoll.FNA`'s renderer, built for MonoGame, and draws the same pixels.

```csharp
using PaperDoll.MonoGame;

var textures = new PuppetTextures(GraphicsDevice, puppet);   // one per loaded puppet
var renderer = new PuppetRenderer(GraphicsDevice);           // one for the game
renderer.Draw(instance, textures, placement);                // puppet space to viewport pixels
```

Use the HiDef graphics profile (`GraphicsDeviceManager.GraphicsProfile = GraphicsProfile.HiDef`; MonoGame defaults to Reach). The target drawn into must keep its contents (`RenderTargetUsage.PreserveContents`), since each puppet is drawn through an offscreen layer.

**MonoGame is not a dependency of this package.** It is built against MonoGame 3.8.6; reference the MonoGame platform package your game uses (`MonoGame.Framework.DesktopGL` or `MonoGame.Framework.WindowsDX`). Other backends are not supported: `PuppetRenderer` throws `NotSupportedException` on them.

BSD 2-Clause, a port by behaviour of Inochi2D and nijilive (BSD 2-Clause); `LICENSE.md` and `attribution.md` in the package hold the notices.
