# PaperDoll demo

The demo for [PaperDoll](https://github.com/rathinosk/PaperDoll), a 2D puppet runtime for .NET, FNA and MonoGame. It shows up to five Inochi2D puppets (`.inp`, `.inx`) at once, with an ImGui panel of parameter sliders, physics and animation controls, a texture browser and a blend-mode test scene.

This package holds the demo's binaries only; no puppets. Windows x64; nothing else to install.

| Folder | Demo |
| --- | --- |
| `FNA/` | `PaperDoll.Demo.exe`: the demo on FNA (NativeAOT) |
| `MonoGame-DesktopGL/` | `PaperDoll.Demo.MonoGame.exe`: the demo on MonoGame, OpenGL |
| `MonoGame-WindowsDX/` | `PaperDoll.Demo.MonoGame.exe`: the demo on MonoGame, DirectX 11 |

## Puppets to try

Inochi2D's example models, Aka and Midori, are at <https://github.com/Inochi2D/example-models> (see that repository for their licence). Download a `.inx` file from there, then:

- drop it on the demo's window (one or several files, up to five puppets), or
- in the FNA demo, use the Open button, or
- pass it on the command line: `PaperDoll.Demo.exe Midori.inx Aka.inx`

## Command line

```sh
PaperDoll.Demo.exe [puppet files...]
PaperDoll.Demo.exe Midori.inx --play <animation> --set "Head:: Yaw-Pitch=0.5,0"
PaperDoll.Demo.exe --test-scene                  # every blend mode
PaperDoll.Demo.exe Midori.inx --bench            # frame time, allocations, draw calls, GPU memory
```

Other options: `--screenshot <png>` (with `--frames <n>`) saves a frame and exits, `--no-physics`, `--no-atlas` and `--texture-scale <1|0.5|0.25|0.125>`. The MonoGame demo takes the same options.

## Licence

PaperDoll is BSD 2-Clause; `LICENSE.md` has its notice, the notices of the projects it ports, and the outside code these binaries bundle. The bundled code's licence texts are in `licenses/`.

PaperDoll is made by Rathinosk: <https://rathinosk.com>.
