# PaperDoll design

Why PaperDoll is built the way it is. These pages record the decisions that shape the library and the reasons for them, for contributors and for anyone who wants to know why it behaves as it does. What PaperDoll supports is in [features.md](../features.md); how to use it is in [getting-started.md](../getting-started.md) and [api.md](../api.md).

## Principles

- **A port by behaviour.** PaperDoll reproduces how nijilive (with Inochi2D and nicxlive) loads, evaluates and draws puppets, written anew as idiomatic C#. Where the upstream behaviour is in question, nijilive decides. Where PaperDoll deliberately differs, it is recorded in [nijilive-differences.md](nijilive-differences.md).
- **Correct means the puppets look right.** Behaviour is judged by looking at real puppets in the demo, next to the editors that made them. Tests guard what has been checked; they do not define it ([testing.md](testing.md)).
- **An engine-free core.** `PaperDoll.Core` knows nothing of FNA, MonoGame or any GPU API. It turns a puppet and its inputs into plain arrays that any renderer can draw.
- **Many puppets, no global state.** A loaded puppet is shared, immutable data; every puppet on screen is an instance with its own clock, parameters, physics and animations ([runtime-model.md](runtime-model.md)).
- **The host owns the game.** PaperDoll holds no game state, scripting, input or save format. The host sets parameter values and plays animations; PaperDoll evaluates them.
- **Reads puppets, never writes them.** Authoring stays in the existing tools.
- **Cheap enough for a weak machine.** No managed allocations per frame once warmed up, compact GPU memory, few draw calls ([rendering.md](rendering.md#performance)).
- **A small, stable public API.** Internals stay internal until a consumer needs them; every public member is documented.

## Pages

| Page | Covers |
| --- | --- |
| [format.md](format.md) | Which format PaperDoll reads, which upstream is its authority, what is in and out of scope, and how odd data is treated |
| [runtime-model.md](runtime-model.md) | Loaded puppets and instances, time steps, threading, when evaluation runs |
| [evaluation.md](evaluation.md) | Parameters and keyforms, MeshGroups, physics and animations: the order they run in and how they combine |
| [rendering.md](rendering.md) | Core's output, draw order, layers, masks, blend modes, texture atlases and the performance choices behind them |
| [platform.md](platform.md) | Target framework, dependencies, FNA and MonoGame, shaders, packaging and licence |
| [testing.md](testing.md) | How correctness is checked and kept |
| [nijilive-differences.md](nijilive-differences.md) | Every deliberate difference from nijilive, in one list |
