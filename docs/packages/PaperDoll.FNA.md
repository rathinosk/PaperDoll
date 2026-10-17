# PaperDoll.FNA

Draws PaperDoll puppets through [FNA](https://fna-xna.github.io/): Parts, Composite layers, masks and all nineteen blend modes of the format, with each puppet's textures cropped and packed into atlas pages so many parts share a draw call. Allocation-free per frame once warmed up.

```csharp
var textures = new PuppetTextures(GraphicsDevice, puppet);   // one per loaded puppet
var renderer = new PuppetRenderer(GraphicsDevice);           // one for the game
renderer.Draw(instance, textures, placement);                // puppet space to viewport pixels
```

The target drawn into must keep its contents (`RenderTargetUsage.PreserveContents`), since each puppet is drawn through an offscreen layer.

**FNA is not a dependency of this package.** It is built against FNA 26.10; FNA is not on NuGet, so the game references FNA itself (from source, as FNA recommends) and ships FNA's native libraries.

BSD 2-Clause, a port by behaviour of Inochi2D and nijilive (BSD 2-Clause); `LICENSE.md` and `attribution.md` in the package hold the notices.
