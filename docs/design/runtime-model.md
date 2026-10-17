# Runtime model

Games show several puppets at once, sometimes the same character twice, and may update them at different rates. nijilive and nicxlive keep the clock, camera, viewport and vertex buffers global, so their puppets cannot run at different rates or be drawn with different cameras. PaperDoll settles this before anything else, because every later part depends on it.

## Puppet and instance

- **`Puppet`** is the loaded file: node tree, meshes, textures, parameters, bindings, animations, extension blobs and the load report. It is immutable after `Puppet.Load` and may be shared across threads. Data derived from it once, such as the MeshGroup lookup grids, is built on first use and shared.
- **`PuppetInstance`** is one puppet on screen, made by `Puppet.CreateInstance()`. It holds everything that changes: parameter values, animation playbacks, physics state and the output arrays. Any number of instances share one `Puppet` and never share state. An instance is not thread-safe.

So the same character twice costs one load, and an instance costs its parameter values plus its deformed vertex buffers.

Parameter values are set on the instance (`SetParameterValue`), not on the shared `Parameter` objects as nijilive does. A value outside a parameter's range is kept as set (and read back as set) and evaluated at the nearest end of the range.

## No global state

- **No global clock.** `PuppetInstance.Update(deltaSeconds)` advances that instance only: its animations, and its physics in small steps of its own.
- **No global camera or render state.** Core's output is in puppet space; the camera and viewport are arguments of the renderer's draw call.
- **Loading is self-contained**, so puppets can be loaded on several threads.

## Textures decoded on demand

A loaded puppet keeps its textures encoded; `PuppetTexture.Decode()` gives RGBA when asked. The renderer decodes, uploads and drops the pixels, so a loaded puppet's memory is about its file plus its tree.

## When evaluation runs

`Update` evaluates the instance only when its output can have changed: after a parameter is set or reset, on every update while physics or an animation is running, and not otherwise. A paused animation does not force evaluation. Evaluation writes into buffers the instance allocated once; it allocates nothing.

## The host's values belong to the host

What the host sets is never overwritten by animations: they act on a copy, and `GetParameterValue` keeps returning what the host set. Physics is the exception, as in nijilive: a parameter a physics node drives takes the simulated value while physics is on, so reading it back shows what physics did. `GetEvaluatedValue` gives the value actually used. See [evaluation.md](evaluation.md#animations).
