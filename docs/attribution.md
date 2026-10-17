# Attribution

## Where PaperDoll comes from

PaperDoll is a C# port, by behaviour, of three open-source 2D puppet runtimes:

| Project | Copyright | Licence |
| --- | --- | --- |
| [Inochi2D](https://github.com/Inochi2D/inochi2d) | © 2020-2026 Inochi2D Project; © 2020-2026 Kitsunebi Games | BSD 2-Clause |
| [nijilive](https://github.com/nijigenerate/nijilive) | © 2020 Inochi2D Project; © 2024 nijigenerate Project | BSD 2-Clause |
| [nicxlive](https://github.com/nijigenerate/nicxlive) | © 2020 Inochi2D Project; © 2024 nijigenerate Project | BSD 2-Clause |

PaperDoll follows how they load, evaluate and draw puppets, but is written anew in C#, with no code copied from them. nijilive is the main reference; Inochi2D and nicxlive are second readings. The behaviour follows each project's `main` branch as of October 2026 (Inochi2D at `v0.9.0`). Their copyright notices and licence texts are in [`LICENSE.md`](../LICENSE.md), alongside PaperDoll's own BSD 2-Clause licence.

Where PaperDoll deliberately behaves differently from nijilive, the difference and its reason are in [design/nijilive-differences.md](design/nijilive-differences.md).

`PaperDoll.MonoGame` has no sources of its own: it compiles `PaperDoll.FNA`'s files for MonoGame, so the same attribution applies to it.

## Outside code

| Component | Used by | Licence |
| --- | --- | --- |
| [StbImageSharp](https://github.com/StbSharp/StbImageSharp) | `PaperDoll.Core`, to decode PNG and TGA textures | Public domain or MIT |
| [FNA](https://fna-xna.github.io/) | `PaperDoll.FNA` is built against it; the game brings FNA itself | Ms-PL |
| [MonoGame](https://monogame.net/) | `PaperDoll.MonoGame` is built against it; the game brings its MonoGame package | Ms-PL |

The demos in the repository also use FNA's native libraries (zlib), the DirectX 12 Agility SDK (Microsoft's redistributable licence) and [ImGui.NET](https://github.com/ImGuiNET/ImGui.NET) with cimgui and Dear ImGui (MIT); the demo's ImGui renderer is adapted from ImGui.NET's XNA sample (MIT). These are not part of the packages; their licence texts sit beside them in `lib/` and are listed in `LICENSE.md`.
