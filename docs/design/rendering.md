# Rendering

## Core's output: puppet-space arrays

After each update an instance exposes a **draw list**, back to front: one item per visible Part with its vertex positions already in puppet space, UVs and indices, texture slot, opacity, tints, emission strength, mask threshold, blend mode, resolved masks and z-sort; a begin and an end item around each visible Composite (the end item carries the Composite's own opacity, tints, blend mode and masks); the **mask sources** with their shapes; and the puppet-space **bounds**. The renderer adds only the camera.

Why arrays transformed on the CPU rather than a model matrix per Part (nijilive's GPU path): a frame of several puppets is a few tens of thousands of vertices, which the CPU transforms in tens of microseconds, while draw calls dominate. With a matrix per Part every Part is its own draw call; with puppet-space arrays neighbouring Parts share one. MeshGroups deform on the CPU anyway, as in nijilive. It also keeps Core free of any GPU API and lets any renderer use the same output.

Hosts index the draw list rather than `foreach` over it, which would allocate an enumerator.

## Draw order

Draw order follows nijilive's render graph: the puppet's pass holds Parts outside Composites and top-level Composites, sorted by effective z-sort (larger first, so smaller is nearer); a Composite sorts its own members the same way, so they move as one unit. Ties keep the order nijilive queues items in. Inside a Composite nijilive's sort is unstable; PaperDoll keeps ties in gathering order so the result is deterministic.

## One renderer, two frameworks

`PaperDoll.FNA` and `PaperDoll.MonoGame` are one renderer source. The MonoGame project has no sources of its own: it compiles the FNA project's files with `MONOGAME` defined, which changes the namespace and the few framework-specific calls. A renderer change is made once and tested on FNA and on both MonoGame backends, and they draw the same pixels.

## Layers

**Each instance draws into a layer of its own**, as nijilive draws into its own framebuffer, and the layer is drawn onto the host's target with premultiplied blending. So a puppet's masks and blend modes act on that puppet alone: one puppet never masks another, and `ClipToLower` clips to the puppet, not the scene behind it. The renderer switches render targets and restores the host's, so the host's target must preserve its contents (`RenderTargetUsage.PreserveContents`); the renderer checks and throws otherwise.

**A Composite is a layer too**, drawn back like a Part with the Composite's own values.

**Layers are sized to their content.** The renderer plans each layer before drawing (its rectangle, whether anything in it is masked), then clears, draws and copies only that rectangle. Clears are quads, so they cost the rectangle rather than the target. A layer entirely outside the viewport is skipped with everything in it. One render target per nesting depth is kept and grown in coarse steps; pooling a target per layer size was tried and created targets every frame, since sizes change whenever parameters move.

## Blend modes

The advanced modes (Multiply, Screen, Overlay, Darken, Lighten, ColorDodge, ColorBurn, HardLight, SoftLight, Difference, Exclusion) use their real equations, as nijilive draws them on GPUs with advanced blending, which is what the editor shows on most desktop GPUs. FNA and MonoGame have no advanced blend equations, so the renderer copies the layer under the drawable's screen rectangle into a backdrop texture and computes the blended result in the shader. Overlapping triangles within one drawable blend with the backdrop as it was before the drawable, as non-coherent advanced blending does. `PuppetRenderer.AdvancedBlending` switches to nijilive's fixed-function fallback (which draws several of these as Normal) for comparison. The other modes are fixed-function, as nijilive has them.

## Masks

Masks follow nijilive's stencil semantics: `Mask` entries draw only where a source is, `DodgeMask` only where none is; Part sources count where their alpha exceeds their threshold, Mask nodes with their whole mesh; hidden sources still mask. Rather than clearing the stencil for every pass, each pass writes a stencil value not used since the last clear and items test against it, so most clears disappear; a mix of both modes or running out of values clears first. Consecutive items with the same masks share one pass and one batch. The result is the same as nijilive's clear-and-write sequence.

## Textures

- **Transparent outside the texture.** nijilive samples with clamp-to-border and a transparent border; FNA and MonoGame have no border mode. Each texture's region carries a clip rectangle in its vertices, and the shader reads transparent outside it. Clamp-to-edge instead draws streaks wherever UVs leave 0..1.
- **Cropped to visible texels.** Many tools export every Part as a whole-canvas layer that is mostly transparent. Each texture is cropped to the rectangle around its non-zero texels (premultiplied texels with zero alpha can still add colour, so any non-zero byte counts), grown to multiples of 16 so mip levels keep the same texel blocks. That cuts GPU memory several-fold with no visible change.
- **Atlas pages in draw order.** Cropped textures are packed into atlas pages (4096 square, MaxRects) in the order Parts first draw, so Parts drawn one after another usually share a page and a draw call. Packing tallest-first saves no more memory and breaks the runs.
- **Mipmaps on the CPU**, with a box filter that is correct for premultiplied pixels; `preservePixels` selects point sampling.
- **An opt-in texture scale** (1, 1/2, 1/4, 1/8) for machines with little GPU memory.
- Only the albedo is drawn. Emissive and bump textures feed nijilive's optional lighting pass only.

## Pixel exactness

Layers and backdrops are read with point sampling and a quarter-pixel shift, which lands inside the right texel whether a backend puts pixel centres at whole or half coordinates. A Composite with neutral values changes no pixel, and FNA and both MonoGame backends render identical images.

## MonoGame's OpenGL backend

MonoGame's OpenGL backend draws user primitives from client memory, which makes the driver scan and copy every batch, and costs many times what the same frame costs elsewhere. On OpenGL the renderer streams batches through a dynamic vertex and index buffer instead (append without overwrite, discard when full). On DirectX 11 MonoGame streams user primitives itself, so they are used as they are.

## Performance

There is no fixed millisecond budget: a weak machine is the target, and what helps it helps every other. The checklist, measured with the demo's `--bench` (five puppets, physics on, every animation looping, every parameter swept):

1. **No managed allocations per frame** in update or draw once warmed up, guarded by a test.
2. **Less GPU memory**, by cropping textures and the optional texture scale.
3. **Layers sized to their content**, for fill rate on weak GPUs.
4. **Fewer draw calls** wherever the picture does not change: atlas batching, shared mask passes, fewer target switches.

The remaining draw calls are mostly advanced-blend Parts (a backdrop copy and a draw each) and atlas page changes. Batching non-overlapping advanced-blend Parts is possible if a weaker machine needs it.
