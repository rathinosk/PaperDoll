# Deliberate differences from nijilive

PaperDoll follows nijilive, quirks included, unless there is a reason not to. Every place it differs on purpose is listed here. Differences of structure that do not change the result (one tree pass instead of stages, arrays instead of pointer lists, CPU-transformed vertices) are not listed.

## Runtime

| Area | nijilive | PaperDoll | Why |
| --- | --- | --- | --- |
| Clock, camera, render state | Global | Per instance; the camera is an argument of the draw call | Several puppets at different rates and placements |
| Parameter values | Stored on the shared parameter | Stored on the instance | Instances share one loaded puppet |

## Physics

| Area | nijilive | PaperDoll | Why |
| --- | --- | --- | --- |
| Puppet placement | Does not affect physics | Opt-in `ScenePosition` added to anchors; constant gives nijilive's behaviour | Moving a puppet across a scene swings its hair and cloth |
| Initial state | Reset before any parameter applies | At rest in the default pose | A puppet whose defaults move a physics node does not start swinging |

## Animations

| Area | nijilive | PaperDoll | Why |
| --- | --- | --- | --- |
| Forced lanes | Overwrite the parameter's value | Act on a copy; the host's value returns when the animation stops | The host owns its values |
| Strength | Multiplies lane values | Blends from the value underneath | At strength 0 nothing changes for any merge mode; fade-outs do not zero parameters |
| Loop timing | Compares rounded frames | Wraps with the remainder | Independent of update rate; nijilive's loops stick above 120 updates a second |
| Speed | Stored, never applied | Applied | It is part of the playback API |
| Several playbacks | Applied in creation order | Applied in start order | The most recently started wins on a shared axis |
| Keyframe order | Assumed sorted | Sorted at load, with a report | Files may store them out of order |
| Lane `target` | 0 is X | 0 is X (Inochi2D 0.9 reads it as Y; not followed) | What the editors write |

## Parameters

| Area | nijilive | PaperDoll | Why |
| --- | --- | --- | --- |
| Single-axis input on a 2D parameter | Fills the other axis with 1 (an additive input adds 1 to it) | Fills it with 0 when adding, 1 when multiplying | The other axis stays unchanged |
| Zero-width range or axis segment | Divides by zero | Gives 0 | Finite output |

## MeshGroups

| Area | nijilive | PaperDoll | Why |
| --- | --- | --- | --- |
| Degenerate triangle | Fills its grid cells with a map of infinities | Claims no cells, maps nothing | Finite output |

## Rendering

| Area | nijilive | PaperDoll | Why |
| --- | --- | --- | --- |
| Advanced blend modes | `KHR_blend_equation_advanced`, or a fallback that draws several as Normal | The same equations in a shader reading a copy of the backdrop; fallback switchable | FNA and MonoGame have no advanced blend equations |
| Clamp-to-border | Transparent border colour | Transparent clip rectangle per texture region | No border address mode in FNA or MonoGame |
| Ties inside a Composite | Unstable sort | Gathering order | Deterministic output |
| Stencil | Cleared for every mask pass | A fresh stencil value per pass; cleared only when needed | Fewer clears, same result |
| Layers | Full framebuffer | Sized to their content | Fill rate on weak GPUs |
| Textures | As stored | Cropped to visible texels, packed into atlases, optional scale | GPU memory and draw calls |
