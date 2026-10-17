# Supported features

What PaperDoll reads from a puppet file, and what it does with each part of it. `paperdoll validate <file>` reports, for a given puppet, everything in it that PaperDoll skips or ignores.

## Files

PaperDoll reads **INP1** puppets (`.inp` and `.inx`, the same container), as written by Inochi Creator 0.7 and 0.8, nijigenerate and Mannequin Character Generator. It reads puppets; it does not edit or write them.

| Feature | Supported | Notes |
| --- | --- | --- |
| INP1 container | Yes | A JSON payload, a texture section and an optional extension section |
| INP2 (Inochi Creator 0.9) | No | Recognised and rejected with a `PuppetFormatException` that says so |
| PNG textures | Yes | |
| TGA textures (uncompressed and RLE) | Yes | |
| BC7 textures | No | Reported; the slot draws nothing |
| Extension sections (`EXT_SECT`) | Kept | Exposed as named byte blobs in `Puppet.Extensions`, not interpreted |
| Metadata (`meta`) | Yes | Name, version, rigger, artist, rights, licence and contact in `Puppet.Meta`; `preservePixels` selects point (nearest-neighbour) filtering |
| Parameter groups | Yes | Exposed for tools; no effect when drawing |
| Automators (`automation`) | No | Reported |

Textures are stored with premultiplied alpha, and PaperDoll draws them that way. A texture reads transparent outside UVs 0 to 1, so a Part whose UVs reach past its texture draws nothing there.

## Nodes

| Node type | Supported | What it does |
| --- | --- | --- |
| `Node` | Yes | Transform and grouping. `lockToRoot` makes a node take the root's transform instead of its parent's |
| `Part` | Yes | A textured mesh, drawn with its first non-empty texture slot (the albedo). Emissive and bump textures are loaded but not drawn |
| `Composite` | Yes | Draws its contents into a layer of their own, which is then drawn like a Part, with the Composite's opacity, tints, blend mode and masks |
| `MeshGroup` | Yes | Deforms the nodes below it by its own mesh (see [MeshGroups](#meshgroups)) |
| `Mask` | Yes | A mesh used only as a mask source; never drawn |
| `SimplePhysics` | Yes | A pendulum that drives a parameter (see [Physics](#physics)) |
| `GridDeformer`, `PathDeformer` | No | Skipped with their children, reported |
| `DynamicComposite`, `Shapes`, `AnimatedPart` | No | Skipped with their children, reported |
| Editor `Camera` | Ignored | Skipped, reported |
| `pinToMesh`, `weldedLinks` | No | Reported |

The root node is always read as a plain `Node`. Editor-only keys (such as Mannequin Character Generator's `poseID` and `mqnFilename`, or Inochi Creator's `psdLayerPath`) are ignored and listed in `Puppet.Issues`.

**Transforms.** A node's local matrix is translation, then rotation (z, then y, then x), then scale; its world matrix is its parent's times its own. A Part's mesh origin is subtracted from its vertices first. Puppet space has x to the right and y down, in the puppet's own pixels.

**Visibility.** A node is drawn only if it and all its ancestors are enabled.

**Draw order.** Parts are drawn back to front by their effective z-sort: the node's own `zsort` plus its ancestors', plus any `zSort` binding, larger values further back. Ties keep tree order (a depth-first walk with each node's children sorted by z-sort). A Composite's contents are sorted among themselves inside its layer, and the Composite takes its place in the puppet as a single unit at its own z-sort.

## Parameters and bindings

| Feature | Supported | Notes |
| --- | --- | --- |
| 1D and 2D parameters, axis points, defaults | Yes | Values outside a parameter's range act as the nearest end |
| Merge modes: Additive, Weighted, Multiplicative, Forced, Passthrough | Yes | How inputs from physics and animations combine with the value the game sets |
| Keyform interpolation: Nearest, Linear, Cubic | Yes | Per binding |
| Unset keyforms | Yes | The values stored in the file are used as they are (editors fill them in when saving) |
| Parameter-to-parameter bindings | Read | Not evaluated; `validate` lists them |

On each update, every parameter takes the value the game set, merged with any inputs: additive inputs are added, multiplicative and weighted ones multiply it (averaged by weight), and a forced input replaces it. The value is mapped onto the parameter's keyform grid, and each binding is interpolated there:

- **Nearest**: the nearer keyform (the later one at exactly halfway).
- **Linear**: linear between keyforms; bilinear on a 2D parameter.
- **Cubic**: a Catmull-Rom curve through the keyforms, clamped at the ends; bicubic on a 2D parameter.

A 1D parameter uses only its X axis. What each binding target does:

| Target | Applies to | Effect |
| --- | --- | --- |
| `transform.t.x/y/z`, `transform.r.x/y/z` | Every node | Added to the node's translation and rotation (radians) |
| `transform.s.x/y` | Every node | Multiplies the node's scale |
| `zSort` | Every node | Added to the node's z-sort; its children inherit it |
| `opacity`, `tint.r/g/b` | Parts, Composites | Multiplies the node's value, clamped to 0..1 |
| `screenTint.r/g/b` | Parts, Composites | Added to the node's value, clamped to 0..1 |
| `emissionStrength` | Parts, Composites | Multiplies the node's value; carried in the draw list, not drawn |
| `alphaThreshold` | Parts, Composites | Read; it does not change the mask threshold |
| `deform` | Parts, MeshGroups, Mask nodes | Per-vertex offsets added to the mesh before its transform; ignored if its length does not match the mesh |
| `gravity`, `length`, `frequency`, `angleDamping`, `lengthDamping`, `outputScale.x/y` | `SimplePhysics` | Gravity, frequency, damping and output scale multiply the node's setting; length is added to it |

Non-finite `deform` offsets in a file are rebuilt at load from the neighbouring vertices.

## MeshGroups

A MeshGroup bends what is below it with its own mesh: when its `deform` bindings move its vertices, each of its triangles moves whatever lies inside it by the same affine map, from the triangle at rest to the triangle deformed. The MeshGroup itself draws nothing.

| Feature | Supported | Notes |
| --- | --- | --- |
| Deforming Parts, Mask nodes and nested MeshGroups | Yes | Each vertex inside the mesh follows the triangle it lies in; vertices outside the mesh stay where they are |
| `translate_children` | Yes | Other nodes below it are moved as a whole, by where their origin lands |
| `dynamic_deformation` | Yes | Off: a Part's rest vertices are mapped and its own `deform` offsets added on top. On: its deformed vertices are mapped |
| `propagate_meshgroup` on Composites | Yes | The Composite passes the MeshGroup on to its contents (the Composite itself is not moved) |

A MeshGroup reaches its children, and through any child that is a Part, a Mask node or a propagating Composite, their children too; plain nodes, physics nodes and nested MeshGroups stop it (a nested MeshGroup is deformed itself, and acts on its own children). Each node is reached by at most one MeshGroup.

Order within an update: parameters first, then down the tree, parents before children: a node's transform, a non-dynamic MeshGroup acting on it, then a dynamic one. A nested MeshGroup therefore takes in what a non-dynamic MeshGroup above it does to its mesh, but not what a dynamic one does.

To find the triangle under a position quickly, PaperDoll lays a grid of one-unit cells over the MeshGroup's rest mesh and gives each cell one triangle, so a position very close to an edge between two triangles may follow the neighbouring one. Triangles with no area move nothing. The grid is built once per loaded puppet and shared by its instances.

## Drawing

| Feature | Supported | Notes |
| --- | --- | --- |
| Opacity, tint, screen tint | Yes | Parts and Composites |
| Masks: `Mask` and `DodgeMask`, `mask_threshold`, Mask nodes | Yes | A puppet's masks never affect another puppet |
| Blend modes | All 19 | Below |
| Emission and lighting | No | Emission strength is in the draw list; neither renderer draws it |
| Texture filtering | Yes | Mipmapped and linear, or point when the puppet sets `preservePixels` |

A Part is drawn as its texel, screen-tinted (`1 - (1 - colour) * (1 - screenTint * alpha)`), then multiplied by its tint and opacity, and blended by its blend mode.

**Layers.** Each puppet is drawn into an offscreen layer of its own, which is then drawn onto the scene. A puppet's blend modes and masks therefore act only within that puppet: `ClipToLower` clips to the puppet's own lower parts, not to the scene behind it. A layer covers only the pixels the puppet covers on screen, so drawing costs follow the puppet's size on screen.

**Masks.** Each entry in a Part's or Composite's `masks` names a source and a mode: `Mask` draws only where a source is, `DodgeMask` only where no source is. A Part masks where its texture's alpha is above its `mask_threshold`; a Mask node masks with its whole mesh. A source masks whether or not it is drawn itself (hidden and disabled sources still mask), and its own masks do not apply to it. Only Parts and Mask nodes can be sources; any other source, or one missing from the puppet, is left out and reported.

**Blend modes.** With premultiplied colours (`S`, `D`) and alphas (`Sa`, `Da`) for the drawn Part and what is below it in the layer:

| Mode | Result |
| --- | --- |
| `Normal` | `S + D (1 - Sa)` |
| `LinearDodge` | `S + D (1 - S)`; alpha `Sa + Da (1 - Sa)` |
| `AddGlow` | `S + D`; alpha `Sa + Da (1 - Sa)` |
| `Subtract` | `D - S (1 - D)`; alpha `Da + Sa (1 - Da)` |
| `Inverse` | `S (1 - D) + D (1 - Sa)`; alpha `Sa (1 - Da) + Da (1 - Sa)` |
| `DestinationIn` | `D Sa` (keeps what is below only where the Part is) |
| `ClipToLower` | `S Da + D (1 - Sa)` (draws only over what is below) |
| `SliceFromLower` | `D (1 - Sa)` (cuts the Part's shape out of what is below) |
| `Multiply`, `Screen`, `Overlay`, `Darken`, `Lighten`, `ColorDodge`, `ColorBurn`, `HardLight`, `SoftLight`, `Difference`, `Exclusion` | The standard separable blend function `f` of each name (as in the W3C Compositing and Blending specification), composited as `f(Cs, Cd) Sa Da + S (1 - Da) + D (1 - Sa)`, alpha `Sa + Da - Sa Da`, where `Cs` and `Cd` are the colours without premultiplication |

The last group is drawn in a shader that reads a copy of the layer below. `PuppetRenderer.AdvancedBlending = false` draws it with cheaper fixed-function approximations instead: Multiply, Screen, Lighten, ColorDodge and Exclusion approximate their equations, and Overlay, Darken, ColorBurn, HardLight, SoftLight and Difference draw as Normal.

## Physics

A `SimplePhysics` node hangs a pendulum from its own origin (the anchor) and drives its parameter from where the pendulum's bob is. The node draws nothing.

| Setting | Default | Meaning |
| --- | --- | --- |
| `model_type` | `Pendulum` | `Pendulum`: a rigid pendulum. `SpringPendulum`: the bob hangs on a spring and also stretches |
| `map_mode` | `AngleLength` | How the bob maps onto the parameter (below) |
| `gravity` | 1 | Times the puppet's `physics.gravity` (default 9.8) and `physics.pixelsPerMeter` (default 1000) |
| `length` | 100 | The pendulum's length, in puppet units |
| `frequency` | 1 | The spring's resonant frequency in Hz (`SpringPendulum`) |
| `angle_damping`, `length_damping` | 0.5 | Damping as a share of critical damping, across and along the spring |
| `output_scale` | (1, 1) | Scales the value written to the parameter |
| `local_only` | false | Works in the parent's space: the anchor is the node's own translation, and moving the puppet in the scene does not swing it |

**Map modes.** The bob's direction from the node's origin and its distance relative to the length give the value. `XY`: the direction times the relative length, minus (0, 1), with Y flipped, so hanging at rest gives (0, 0). `YX`: the same with the axes swapped. `AngleLength`: (angle from straight down / π, relative length). `LengthAngle`: the same swapped. Each is multiplied by the output scale.

**Stepping.** Each update's time step (at most 10 s) is cut into 0.01 s ticks plus one shorter tick for the remainder, each a fourth-order Runge-Kutta step; a tick that goes non-finite is discarded. Physics nodes are stepped in tree order after the other parameters apply, and each node's parameter applies at once, so a node further down a chain sees what the ones above it did. While physics is on, the parameter a node drives takes its value from the simulation.

**In the game.** `PuppetInstance.PhysicsEnabled` turns physics on and off. Instances start at rest, hanging in the pose of their default parameters. `ScenePosition` (in puppet units) tells physics where the game has placed the puppet, so hair and cloth swing when the puppet moves across the scene; leave it constant for physics that only follows the puppet's own motion. `ResetPhysics()` starts every pendulum again at rest, for after a jump.

## Animations

| Feature | Supported | Notes |
| --- | --- | --- |
| Playback, looping, lead-in, lead-out | Yes | Per instance; several at once |
| Lane interpolation: Nearest, Stepped, Linear, Cubic, Bezier | Yes | Below |
| Lane merge modes | Yes | Forced (the default), Additive, Multiplicative, Weighted |
| `additive`, `animationWeight` on an animation | Read | No effect; each lane's merge mode decides how it combines |

An animation has a frame duration (`timestep`), a length in frames and lanes, each driving one axis of one parameter with keyframes. Lane values between two keys:

| Interpolation | Value |
| --- | --- |
| `Nearest` | The later key once past halfway |
| `Stepped` | The earlier key, until the next key's frame |
| `Linear` | Linear between the keys |
| `Cubic` | A Catmull-Rom curve through the key values, clamped at the ends |
| `Bezier` | Linear, eased by the later key's tension: 0.5 is linear, 0 eases in and out, above 0.5 is fast at the keys |

Before the first key and after the last, a lane holds the nearest key's value. A lane whose parameter is missing, or that drives the Y axis of a 1D parameter, is not played.

**Playback.** `Play(loop, playLeadOut)` starts from frame 0, or resumes a paused playback. A one-shot ends on its last frame, or holds it until stopped when `playLeadOut` is false. A loop plays its lead-in once, then repeats up to its lead-out (or its end); its timing does not depend on the update rate. `Stop` lets a looping animation with a lead-out finish by playing the lead-out, or stops at once when asked to. `Pause`, `Seek`, `Speed` and `Strength` (0 to 1) complete it.

**Combining.** Each update, PaperDoll copies the game's parameter values, applies every running animation's lanes to the copy in the order they were started, and evaluates the result. The game's own values are never changed, so a stopped animation hands its parameters back. A Forced lane replaces the value under it, blended by the playback's strength; Additive adds `value * strength`; Multiplicative and Weighted multiply by `1 + (value - 1) * strength`. A lane on one axis leaves the other axis alone. Physics still drives the parameters it drives. `GetEvaluatedValue` reads what a parameter was last evaluated at.

## Runtime

- A loaded `Puppet` is immutable and shared by any number of `PuppetInstance`s; the same character can be on screen twice without being loaded twice.
- Each instance is updated with its own time step. There is no global clock, camera or render state, so instances never affect each other.
- An instance is evaluated on every update while physics or an animation is running, and otherwise only after its parameters change.
- Updating and drawing allocate no managed memory once warmed up.
