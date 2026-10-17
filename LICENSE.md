# Licence

PaperDoll is licensed under the BSD 2-Clause licence below. It is a port, by behaviour, of Inochi2D, nijilive and nicxlive. Those projects are written in D and C++, and PaperDoll is written from scratch in C#, so no code is copied from them; it reimplements what their code does. Because the behaviour and structure come from their work, their notices follow PaperDoll's own and are kept as their licences require.

| Section | Covers | Licence |
| --- | --- | --- |
| [PaperDoll](#paperdoll) | PaperDoll's own code | BSD 2-Clause |
| [Upstream notices: Inochi2D](#inochi2d) | The behaviour and structure PaperDoll ports | BSD 2-Clause |
| [Upstream notices: nijilive](#nijilive) | The behaviour and structure PaperDoll ports (format authority) | BSD 2-Clause |
| [Upstream notices: nicxlive](#nicxlive) | The behaviour and structure PaperDoll ports (second reading) | BSD 2-Clause |
| [Third-party components](#third-party-components) | Bundled and referenced outside code: FNA, fnalibs (FNA3D, MojoShader, SDL3, D3D12 Agility SDK), MonoGame and the packages it brings, ImGui.NET, cimgui, Dear ImGui, StbImageSharp | Ms-PL, zlib, MIT, LGPL, BSD, Microsoft DirectX terms, public domain or MIT (per component) |

## PaperDoll

```text
BSD 2-Clause License

Copyright (c) 2026, Rathinosk
All rights reserved.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are met:

1. Redistributions of source code must retain the above copyright notice, this
   list of conditions and the following disclaimer.

2. Redistributions in binary form must reproduce the above copyright notice,
   this list of conditions and the following disclaimer in the documentation
   and/or other materials provided with the distribution.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS"
AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE
IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE
FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL
DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR
SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER
CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY,
OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
```

## Upstream notices

### Inochi2D

```text
BSD 2-Clause License

Copyright (c) 2020-2026, Inochi2D Project
Copyright (c) 2020-2026, Kitsunebi Games
All rights reserved.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are met:

1. Redistributions of source code must retain the above copyright notice, this
   list of conditions and the following disclaimer.

2. Redistributions in binary form must reproduce the above copyright notice,
   this list of conditions and the following disclaimer in the documentation
   and/or other materials provided with the distribution.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS"
AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE
IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE
FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL
DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR
SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER
CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY,
OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
```

### nijilive

```text
BSD 2-Clause License

Copyright (c) 2020, Inochi2D Project
Copyright (c) 2024, nijigenerate Project
All rights reserved.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are met:

1. Redistributions of source code must retain the above copyright notice, this
   list of conditions and the following disclaimer.

2. Redistributions in binary form must reproduce the above copyright notice,
   this list of conditions and the following disclaimer in the documentation
   and/or other materials provided with the distribution.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS"
AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE
IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE
FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL
DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR
SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER
CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY,
OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
```

### nicxlive

```text
BSD 2-Clause License

Copyright (c) 2020, Inochi2D Project
Copyright (c) 2024, nijigenerate Project
All rights reserved.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are met:

1. Redistributions of source code must retain the above copyright notice, this
   list of conditions and the following disclaimer.

2. Redistributions in binary form must reproduce the above copyright notice,
   this list of conditions and the following disclaimer in the documentation
   and/or other materials provided with the distribution.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS"
AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE
IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE
FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL
DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR
SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER
CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY,
OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
```

## Third-party components

Outside code keeps its own licence. Bundled code in `lib/` has its licence files beside it; packages are fetched from NuGet and are listed here so their terms travel with PaperDoll's.

| Component | Where | Used by | Licence | Licence text |
| --- | --- | --- | --- | --- |
| FNA 26.10 (© 2009-2024 Ethan Lee and the MonoGame Team) | `lib/FNA` (managed source of `FNA.Core.csproj` only) | `PaperDoll.FNA` | Ms-PL | `lib/FNA/licenses/LICENSE` |
| Mono.Xna code in FNA (© 2006 The Mono.Xna Team) | `lib/FNA/src` | `PaperDoll.FNA` | MIT | `lib/FNA/licenses/monoxna.LICENSE` |
| LzxDecoder in FNA (© 2003-2004 Stuart Caie, © 2011 Ali Scissons) | `lib/FNA/src` | `PaperDoll.FNA` | LGPL 2.1 or Ms-PL | `lib/FNA/licenses/lzxdecoder.LICENSE` |
| FNA stock effects (Microsoft) | `lib/FNA/src/Graphics/Effect/StockEffects` | `PaperDoll.FNA` | Ms-PL | `lib/FNA/src/Graphics/Effect/StockEffects/LICENSE` |
| SDL2#, SDL3-CS, FAudio#, Theorafile#, dav1dfile# bindings (© Ethan Lee, Colin Jackson, Luigi Auriemma, Evan Hemsley and the MonoGame Team) | `lib/FNA/lib/*` | `PaperDoll.FNA` | zlib | `LICENSE` in each folder |
| StbImageSharp 2.30.15 (StbSharp; a port of Sean Barrett's stb_image) | NuGet | `PaperDoll.Core` | Public domain or MIT, as its README states | <https://github.com/StbSharp/StbImageSharp> |
| FNA3D (© 2020-2024 Ethan Lee), Windows x64 build from fnalibs 26.10 | `lib/fnalibs/x64/FNA3D.dll` | Demo | zlib | `lib/fnalibs/licenses/FNA3D.LICENSE` |
| MojoShader, inside FNA3D (© 2008-2025 Ryan C. Gordon) | `lib/fnalibs/x64/FNA3D.dll` | Demo | zlib | `lib/fnalibs/licenses/MojoShader.LICENSE.txt` |
| SDL 3.4.19 (© 1997-2026 Sam Lantinga), Windows x64 build from fnalibs 26.10 | `lib/fnalibs/x64/SDL3.dll` | Demo | zlib | `lib/fnalibs/licenses/SDL3.LICENSE.txt` (from SDL's `release-3.4.18` tag; same text on the `release-3.4.x` branch) |
| DirectX 12 Agility SDK 1.619.1 (`D3D12Core.dll`, Microsoft), from fnalibs 26.10 | `lib/fnalibs/D3D12/` | Demo | Microsoft Software License Terms (DirectX), redistributable object code with conditions | `lib/fnalibs/licenses/D3D12.LICENSE.txt` (from nuget.org, `Microsoft.Direct3D.D3D12` 1.619.1) |
| MonoGame 3.8.6 (© 2009-2026 MonoGame Foundation, Inc), `MonoGame.Framework.DesktopGL` and `MonoGame.Framework.WindowsDX` | NuGet | `PaperDoll.MonoGame` (compiled against, not bundled); MonoGame demo and tests | Ms-PL, with MIT parts | <https://github.com/MonoGame/MonoGame/blob/develop/LICENSE.txt> |
| SDL 2.32.10 (© 1997-2025 Sam Lantinga), `MonoGame.Library.SDL` 2.32.10.2 | NuGet, via MonoGame DesktopGL | MonoGame demo and tests | zlib | `LICENSE.txt` in the package |
| OpenAL Soft 1.24.3, `MonoGame.Library.OpenAL` 1.24.3.4 | NuGet, via MonoGame DesktopGL | MonoGame demo and tests (a native library loaded at run time; the demo plays no audio) | LGPL 2 or later, with BSD 3-Clause portions | <https://github.com/kcat/openal-soft/blob/master/COPYING>; `LICENSE` in the package |
| NVorbis 0.10.4 (© 2020 Andrew Ward) | NuGet, via MonoGame DesktopGL | MonoGame demo and tests | MIT | `LICENSE` in the package |
| SharpDX 4.2.0 (© 2010-2016 Alexandre Mutel): `SharpDX`, `.DXGI`, `.Direct2D1`, `.Direct3D11`, `.MediaFoundation`, `.XAudio2`, `.XInput` | NuGet, via MonoGame WindowsDX | MonoGame demo and tests on WindowsDX | MIT | <http://sharpdx.org/License.txt> |
| mgfxc (`dotnet-mgfxc` 3.8.6, MonoGame Foundation) | NuGet, a local dotnet tool | Compiling `PaperDoll.MonoGame`'s effects by hand; not shipped | Ms-PL | <https://github.com/MonoGame/MonoGame/blob/develop/LICENSE.txt> |
| ImGui.NET 1.91.6.1 (© 2017 Eric Mellino and ImGui.NET contributors) | `lib/ImGui.NET/ImGui.NET.dll` | Demo | MIT | `lib/ImGui.NET/LICENSE` |
| cimgui (© 2015 Stephan Dilly), Windows x64 build from the ImGui.NET 1.91.6.1 package | `lib/ImGui.NET/win-x64/cimgui.dll` | Demo | MIT | `lib/ImGui.NET/cimgui.LICENSE` (from cimgui's `1.91.6` tag) |
| Dear ImGui 1.91.6, inside cimgui (© 2014-2024 Omar Cornut) | `lib/ImGui.NET/win-x64/cimgui.dll` | Demo | MIT | `lib/ImGui.NET/imgui.LICENSE.txt` (from Dear ImGui's `v1.91.6` tag) |
| ImGui.NET's XNA sample renderer, adapted (© 2017 Eric Mellino and ImGui.NET contributors) | `samples/PaperDoll.Demo/ImGuiRenderer.cs` | Demo | MIT | `lib/ImGui.NET/LICENSE` |

The demo ships FNA's Windows x64 native libraries only (FAudio and libtheorafile are left out: the demo plays no audio or video). No library package (`PaperDoll.Core`, `PaperDoll.FNA`, `PaperDoll.MonoGame`) bundles FNA, MonoGame, any of the native libraries or ImGui: `PaperDoll.FNA` is built against FNA 26.10 and `PaperDoll.MonoGame` against MonoGame 3.8.6, and the game that uses them brings the framework and its native libraries with their notices. Test-only packages (xunit.v3, Apache 2.0) are not shipped.
