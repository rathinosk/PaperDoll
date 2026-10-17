# Testing

## Correctness is judged by eye

The reference runtimes are read, never built or run: they are written in D and C++, and PaperDoll stays pure C#. There is therefore no golden output to compare against. Correctness is judged by looking at real puppets in the demo, next to the editor that made them: if the puppets move and look as their authors intended, PaperDoll is correct. Where a faithful port looks wrong, the visual judgement wins and the difference is recorded ([nijilive-differences.md](nijilive-differences.md)).

Automated tests then guard what has been checked against later breakage. They do not define correctness.

## What the tests cover

- **Maths, without a device**: keyform location and interpolation weights, merge modes, MeshGroup maps, physics integrators, lane sampling and playback timing, draw order. Puppets are built in memory, so these need no files.
- **Sample puppets**: load, sweep every parameter's range for finite output, and play every animation. Sample puppets are not in the repository (their licences vary and they are large); these tests read them from `.samples/` (or `PAPERDOLL_SAMPLES`) and skip when they are absent.
- **Render tests**, on a hidden window: every blend mode against its equation over opaque and half-transparent backdrops, masks, Composite layers, atlas packing, and pixel exactness. They run on FNA and on MonoGame's DesktopGL and WindowsDX backends from the same test source, and skip when no graphics device can be created.
- **Allocation tests**: updating and drawing a warmed-up puppet with physics on, animations playing and parameters moving allocates nothing.
- **Shader freshness**: a test fails when `PaperDoll.fx` changes without its compiled effects being rebuilt.

## Snapshots

A snapshot records an instance's inputs (parameter values, running animations, the sequence of update steps) and Core's output (evaluated values, the draw list, masks, layers, bounds) as JSON. One is saved from the demo once a pose has been checked by eye, and replayed by the tests and by `paperdoll snapshots`; floats compare within 1e-4. Snapshots live beside the sample puppets, not in the repository. When a deliberate change moves output, the affected snapshots are checked again and re-saved. The format is versioned, and older versions still compare on the fields they have.

## The demo as a test bench

The demo opens up to five puppets at once, each placed and scaled on its own, with sliders for every parameter, physics and animation controls, a texture browser, and a blend test scene that covers every blend mode, mask mode and Composite case real puppets may lack. Command-line options (`--set`, `--play`, `--frames`, `--screenshot`, `--bench`) make a pose or a measurement repeatable.
