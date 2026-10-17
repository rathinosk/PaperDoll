# API overview

A map of PaperDoll's public types: what each is for, how long it lives, and in what order a game calls them. Every public member has an XML doc comment, shipped in the packages, so your IDE shows the details; this page is the overview those comments do not give. [getting-started.md](getting-started.md) walks through a complete example.

## Namespaces

| Namespace | Package | Contents |
| --- | --- | --- |
| `PaperDoll` | `PaperDoll.Core` | Everything engine-free: loading, the node tree, parameters, animations, physics and the draw list |
| `PaperDoll.Fna` | `PaperDoll.FNA` | `PuppetTextures` and `PuppetRenderer` for FNA |
| `PaperDoll.MonoGame` | `PaperDoll.MonoGame` | The same two types, with the same members, for MonoGame |

Core uses `System.Numerics` (`Vector2`, `Vector3`); the renderers take XNA's `Matrix` for placement. Code written against one renderer compiles against the other by changing the `using`.

## Lifetimes

```text
Puppet.Load(file)                    once per file           immutable; share it freely
 ├─ puppet.CreateInstance()          once per copy on screen  all the changing state
 └─ new PuppetTextures(device, puppet)  once per loaded puppet  GPU textures, shared by its instances; Dispose
new PuppetRenderer(device)           once per game           draws any instance of any puppet; Dispose
```

Nothing is global. Two instances of one puppet never affect each other, and you can create and drop instances at any time; an instance holds no GPU resources and needs no disposal.

## Each frame

```csharp
instance.SetParameterValue(parameter, value);   // any number, any time before Update
instance.ScenePosition = placement / scale;     // optional: lets physics swing when you move the puppet
instance.Update(deltaSeconds);                  // animations, parameters, physics, deformation, draw list
renderer.Draw(instance, textures, transform);   // back to front, after every Update of the frame
```

`Update` and `Draw` allocate nothing once warmed up. `Draw` reads the instance as of its last `Update`, so update every instance first and then draw them; drawing the same instance twice (a reflection, a thumbnail) costs no extra evaluation.

## Loading

| Member | Notes |
| --- | --- |
| `Puppet.Load(string path)`, `Load(Stream)`, `Load(ReadOnlyMemory<byte>)` | Reads `.inp` and `.inx`. The memory overload keeps referring to the bytes you pass, so do not change them afterwards |
| `PuppetFormatException` | Thrown when the data is not a readable INP1 puppet (INP2 files included). The only exception loading throws for bad data |
| `Puppet.Issues` (`LoadIssue`, `LoadIssueKind`) | Everything the loader skipped, ignored or repaired. A puppet with issues still loads; log them while developing |

Loading touches no shared state, so puppets can be loaded on background threads and handed to the game thread.

## The loaded puppet (read only)

| Type | What it holds |
| --- | --- |
| `Puppet` | `Root` and `Nodes` (depth first), `Parameters`, `ParameterGroups`, `Animations` (by name), `Textures`, `Extensions` (raw `EXT_SECT` blobs), `Meta`, `Physics`. Lookups: `FindParameter(name)`, `TryGetParameter(uuid)`, `TryGetNode(uuid)` |
| `PuppetMeta`, `PuppetUsageRights` | Name, version, artist, rigger, copyright, licence URL, contact, and the usage rights the author set. PaperDoll reports them and enforces nothing |
| `Node` and its subclasses `Part`, `Composite`, `MeshGroup`, `Mask`, `SimplePhysics` (`Drawable` is the base of those with a mesh) | The node tree as stored. `Kind` (`NodeKind`) says which type a node is; `Name`, `Uuid`, `Enabled`, `ZSort`, `Transform`, `Parent`, `Children` |
| `MeshData` | A mesh's vertices, UVs, triangle indices and origin |
| `Parameter` | `Name`, `IsVec2`, `Min`, `Max`, `Defaults`, `MergeMode`, axis points, its `Bindings`, and `PhysicsDriver` if a physics node drives it |
| `ParameterBinding` (`ValueBinding`, `DeformBinding`, `ParameterAxisBinding`) | What a parameter moves and its keyform values. For tools and inspection; games rarely need them |
| `Animation`, `AnimationLane`, `Keyframe` | An animation's timing, lead-in and lead-out, and its lanes of keyframes |
| `PuppetTexture`, `TextureImage` | A texture slot, kept encoded. `Decode()` returns premultiplied RGBA; the result is not cached |

Every one of these is shared by all instances of the puppet, and none of them changes after loading. Setting a value is done on the instance, never on these objects.

## The instance

`PuppetInstance` is where a game spends its time.

| Area | Members |
| --- | --- |
| Parameters | `SetParameterValue(parameter, value)`, `GetParameterValue` (what you set, or what physics gave it), `GetEvaluatedValue` (what the last update used, animations included), `ResetParameters()` |
| Animations | `Animations` (one `AnimationPlayback` per animation), `GetAnimation(name)`, `TryGetAnimation`, `PlayAnimation(name, loop)`, `StopAnimations()` |
| Physics | `PhysicsEnabled`, `ScenePosition`, `ResetPhysics()` |
| Time | `Update(deltaSeconds)`, `Time` (the sum of every step) |
| Output | `DrawList`, `MaskSources`, `Bounds` |

`AnimationPlayback` controls one animation on one instance: `Play(loop, playLeadOut)`, `Pause()`, `Stop(immediate)`, `Seek(frame)`, `Speed`, `Strength`, and state such as `IsRunning`, `Frame` and `LoopCount`. Several can run at once; lanes of later-started playbacks win over earlier ones where both force the same parameter.

Look parameters and playbacks up once, at load, and keep the objects: `FindParameter` and `GetAnimation` search by name, the setters do not.

## The draw list (for custom renderers)

You only need these if you draw puppets without `PaperDoll.FNA` or `PaperDoll.MonoGame`.

| Type | What it is |
| --- | --- |
| `DrawItem` | One entry of `DrawList`, back to front. `Kind` (`DrawItemKind`) is `Part`, or `BeginComposite` / `EndComposite` around the items drawn into a Composite's layer. A Part item has `Positions` (puppet space), `Uvs`, `Indices`, `Texture` (slot index), `Opacity`, `Tint`, `ScreenTint`, `BlendMode` and `Masks` |
| `DrawMask`, `MaskSource` | A mask on an item: the source's shape (positions, UVs, indices, texture, `Threshold`) and the `MaskingMode` |
| `PuppetBounds` | The puppet-space rectangle around what is drawn; `Size`, `Center`, `IsEmpty` |

The spans and lists belong to the instance and are rewritten by the next `Update`: read them before then, and copy anything you need to keep. [features.md](features.md#drawing) gives the colour and blend equations a renderer must apply.

## The renderers

| Type | Notes |
| --- | --- |
| `PuppetTextures(device, puppet, useAtlas = true, textureScale = 1f)` | The puppet's textures on the GPU. Uploads on first use; call `LoadAll()` while loading to avoid a stall on the first frame. `textureScale` (0.5, 0.25, 0.125) saves memory for puppets drawn small. `ErrorOf(slot)` says why a slot could not be decoded. Dispose it when the puppet is no longer drawn |
| `PuppetRenderer(device)` | `Draw(instance, textures, transform, filter = null)`. `transform` maps puppet space (the puppet's own pixels, x right, y down) to viewport pixels. `AdvancedBlending` switches the exact blend equations off for cheaper ones; `LastDrawCalls` and `LastLayers` report the cost of the last draw |

`Draw` needs a render target that keeps its contents (`RenderTargetUsage.PreserveContents`, on the back buffer or your `RenderTarget2D`) and throws `InvalidOperationException` if it does not. It restores your render target and viewport, and leaves blend, depth-stencil, rasterizer and sampler states changed as `SpriteBatch` does, so set your own states before drawing anything else.

## Threading

| Object | Rule |
| --- | --- |
| `Puppet` | Immutable; read it from any number of threads |
| `PuppetInstance` | Not thread-safe. Different instances can be updated on different threads at once, even instances of the same puppet |
| `PuppetTextures`, `PuppetRenderer` | Use them on the thread that owns the graphics device, as with any FNA or MonoGame object |

## Units and conventions

- **Puppet space** is the puppet's own pixels: x right, y down, origin wherever the file puts it. Use `instance.Bounds` (at rest, right after creation) to centre or stand a puppet on the ground, as the example in [getting-started.md](getting-started.md) does.
- **Parameter values** are in the parameter's own range (`Min` to `Max`); values outside it are kept and act as the nearest end. The Y component of a 1D parameter is ignored.
- **Rotations** are in radians, **time** in seconds, **animation positions** in frames.
- **Textures** are premultiplied alpha throughout.
- **IDs** (`Uuid`) are the 32-bit IDs stored in the file; names are not guaranteed unique, so `FindParameter` returns the first match.
