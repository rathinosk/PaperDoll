# Evaluation

How an instance turns parameter values, animations and time into a pose. The rule throughout is to port nijilive's behaviour as it is, including quirks a cleaner design might change, because puppets are authored and previewed against it. The few places PaperDoll differs on purpose are listed in [nijilive-differences.md](nijilive-differences.md).

## Order of one update

1. Animations advance and their lanes are applied to a copy of the host's parameter values.
2. Every parameter that no physics node drives is evaluated through its bindings.
3. Each physics node, in tree order, steps and drives its parameter, which is evaluated at once (so a chain sees its parents' results in the same update).
4. Transforms, MeshGroups, visibility and draw order produce the draw list.

nijilive runs these as separate stages over the tree; where they only ever let a node act on nodes below it, one depth-first pass gives the same result and is simpler and cheaper.

## Parameters and keyforms

Each parameter merges the host's value with any pushed inputs (from animations and physics) by its merge mode, maps it to 0..1 over its range (clamped), locates it between its axis points, and lets every binding add its interpolated keyform to its target as an offset. Offsets reset every update and combine as nijilive's do: translation, rotation, z-sort, screen tint and emission strength add; scale, opacity, tint and alpha threshold multiply.

Nearest, Linear and Cubic (Catmull-Rom) interpolation are all linear in the keyforms. PaperDoll therefore computes per-axis weights once per parameter and sums keyform times weight, for single values and whole deform arrays alike, without allocating. Binding modes that belong to animations (Stepped, Bezier) evaluate as nicxlive does.

nijilive's quirks are kept: an `alphaThreshold` binding has no effect (its offset starts at 0 and is multiplied); emission strength's offset starts at 1 and is added to; the merge formula divides weighted inputs by their count rather than their total weight.

## MeshGroups

A MeshGroup moves what lies inside each of its triangles by that triangle's rest-to-deformed affine map. Ported as nijilive has it:

- which nodes it reaches (through Parts, Masks and `propagate_meshgroup` Composites; not through plain nodes or nested MeshGroups);
- drawables have their vertices mapped; other nodes only their origin, and only with `translate_children`;
- non-dynamic MeshGroups map rest vertices and add the node's own deform unmapped; dynamic ones map the deformed vertices;
- vertices outside the mesh are not moved;
- the lookup is nijilive's one-unit grid (the last triangle containing a cell's corner owns the cell), not an exact point-in-triangle test, so edge behaviour matches.

The grid depends only on the loaded puppet, so it is built once and shared by every instance; each instance keeps only its triangle maps and deformation buffers. A degenerate triangle maps nothing (nijilive would fill its cells with infinities).

## Physics

`SimplePhysics` nodes are pendulums or spring pendulums whose bob drives a parameter.

- **Stepping as nijilive does**: each update's time step (at most 10 s) is cut into 0.01 s Runge-Kutta ticks plus one shorter tick for the rest. This is smooth at any frame rate and adds no lag; a strict fixed step, with or without interpolation, was considered and not taken. Each instance steps by its own time step.
- **Integrators and mappings as nijilive has them**, quirks included (the spring pendulum's damping terms, `local_only` measuring from the parent's origin, a failed step driving nothing that update).
- **A scene position**, PaperDoll's own addition: `PuppetInstance.ScenePosition` is where the host placed the puppet, added to every anchor, so moving a puppet across the scene swings its hair and cloth. It never moves the drawing. A constant scene position behaves exactly as nijilive. A host that teleports a puppet calls `ResetPhysics()`.
- **Instances start at rest in their default pose**: the bob hangs below the anchor as the default parameters place it. nijilive resets before any parameter applies, so a puppet whose defaults move a physics node starts swinging there.
- `PhysicsEnabled` turns physics off; driven parameters then keep their last value until the host sets them.

## Animations

- **Animations never overwrite the host's values.** Lanes act on a copy of them, and when a playback stops its parameters return to the host's values. nijilive's Forced lanes overwrite the parameter, leaving it wherever an animation stopped; a host that wants that reads `GetEvaluatedValue` and sets it.
- **Each lane's merge mode decides how it combines.** The animation-level `additive` and `animationWeight` flags are read and exposed but change nothing, as in nijilive, so puppets play as their editor shows them.
- **Strength blends from the value underneath.** At strength 0 a playback has no effect for any merge mode; at 1 its lanes act as authored. nijilive multiplies lane values by the strength, which pulls Forced values towards 0 and zeroes multiplied parameters on a fade-out.
- **Timing does not depend on the update rate.** A loop wraps when it reaches its end, carrying the remainder; nijilive compares rounded frames, which makes its loops stick at high update rates. At 60 updates a second the two show the same frames.
- **Speed is applied** (nijilive stores it but never uses it).
- **Several playbacks run at once**, applied in the order they were started, so a later Forced lane on the same axis wins. Physics still forces the parameters it drives.
- Lane sampling (Nearest, Stepped, Linear, Cubic, Bezier with tension) follows nijilive exactly, including where it differs from binding interpolation.
- nijilive's `snapToFramerate` (sampling at whole frames) is not ported; lanes are sampled at the fractional position.

## Merge inputs on one axis

nijilive fills the other axis of a single-axis input with 1, so an Additive lane on X of a 2D parameter also adds 1 to Y. PaperDoll fills it with what leaves it unchanged: 0 when adding, 1 when multiplying.
