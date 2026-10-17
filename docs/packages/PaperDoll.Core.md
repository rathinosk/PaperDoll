# PaperDoll.Core

The engine-free core of PaperDoll, a 2D puppet runtime for .NET: it loads Inochi2D puppets (`.inp`, `.inx`, the INP1 format written by Inochi Creator, nijigenerate and Mannequin Character Generator), evaluates their parameters, MeshGroup deformation, physics and animations, and produces a draw list per instance: puppet-space vertex positions, UVs, indices, texture slots, opacity, tints, blend modes and masks, back to front.

Fully managed, no reflection, trimming- and AOT-safe, and allocation-free per frame once warmed up. Any number of instances can share one loaded puppet, each with its own time step.

```csharp
var puppet = Puppet.Load("character.inx");
var instance = puppet.CreateInstance();
instance.SetParameterValue(puppet.FindParameter("Head:: Yaw-Pitch")!, new(0.5f, 0));
instance.Update(1f / 60);
for (var i = 0; i < instance.DrawList.Count; i++) { var item = instance.DrawList[i]; /* draw it */ }
```

To draw through FNA, add `PaperDoll.FNA`; through MonoGame, `PaperDoll.MonoGame`. The repository's `docs/` folder has a getting-started guide and the list of supported features.

BSD 2-Clause, a port by behaviour of Inochi2D and nijilive (BSD 2-Clause); `LICENSE.md` and `attribution.md` in the package hold the notices.
