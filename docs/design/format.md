# Format

## INP1, as nijilive reads it

PaperDoll reads **INP1** (`.inp` and `.inx`): the container written by Inochi Creator 0.7 and 0.8, nijigenerate and Mannequin Character Generator, with the Inochi2D 0.8 data model. PNG and TGA (including RLE) textures.

Inochi2D and nijilive have diverged. Inochi2D's main line is now a rewrite with a new container (INP2) and node model, and reads INP1 only through an upgrade step; nijilive kept the 0.8 node model and extended it. The puppets people make today with the tools above are INP1, and nijilive reads all of them, including the nijilive-only keys some tools write (such as `propagate_meshgroup`). So:

- **nijilive is the format and behaviour authority.** Where a field's meaning is in question, nijilive's reading decides.
- **nicxlive** (a C++ port of nijilive) is a second reading, useful where the D code is hard to follow.
- **Inochi2D** (its 0.8-era code and its INP1 upgrade path) is a cross-check, especially for anything nijilive changed after forking.
- **The Inochi2D documentation** is the only written specification of the container and the blend equations. Where it and nijilive's code disagree, the code decides; the documentation describes the editor, whose units are not always the file's (rotation is shown in degrees, stored in radians).
- **Inochi Creator** is a reference for editor-only data and for what its `.inp` export changes, never for runtime behaviour.

INP2 files are recognised and rejected with a clear message rather than misread.

## Scope

In scope: `Node`, `Part`, `Composite`, `MeshGroup`, `Mask` and `SimplePhysics` nodes; parameters with their bindings; animations; the puppet's physics settings and metadata; extension sections, kept as opaque named blobs.

Out of scope: INP2 and Inochi2D 0.9's node model; BC7 textures; nijilive's grid, path and curve deformers, DynamicComposite, Shapes, AnimatedPart and automation; parameter-to-parameter bindings (read, not evaluated). None of them is loaded silently: the loader reports them and `paperdoll validate` lists them, so a puppet that relies on one is easy to spot. Each could be added later on its own if a puppet needs it.

## Odd data is reported, not fatal

A runtime meets files from several editors and versions. The loader keeps going wherever nijilive would, and says what it did:

- Unknown node types are skipped with their children and reported.
- Editor data is ignored and listed as such: Inochi Creator's `Camera` node, parameter groups and parent links, `psdLayerPath`, its extension sections; Mannequin Character Generator's own keys on nodes, bindings and parameters. Parameter groups and parent links stay readable for tools.
- Bindings and physics nodes that point at missing targets are dropped and reported, as nijilive drops them.
- Unknown enum values fall back to the default with a report; enum names match ignoring case and underscores.
- Non-finite deform offsets are rebuilt from neighbouring vertices, as nijilive does.
- Keyframes stored out of order are sorted at load (nijilive samples them as if sorted; only its editor sorts them).

The only fatal errors are a broken container, INP2, and structural errors nijilive also rejects (a child node without a type, keyform arrays whose shape does not match the parameter).

## Stored values are used as stored

Bindings mark some keyforms as unset. nijilive never refills them at runtime: its editor fills them in before saving, and nijilive interpolates what the file holds. PaperDoll does the same. Some tools store values that differ from what nijilive's fill would compute; refilling them at load was tried and looked worse on those puppets, so the file wins.

## Textures are premultiplied

INP1 textures are stored with premultiplied alpha (Inochi Creator premultiplies on import), and nijilive draws them with premultiplied blending without converting them. PaperDoll does the same throughout: decoding, mipmaps, atlases, layers and blending all treat colour as premultiplied.

## `.inx` and `.inp` may differ for the same puppet

An `.inp` exported from Inochi Creator can have MeshGroup deformation baked into its children, flattened parameter groups, pruned nodes and repacked textures. The loader assumes neither form; both are read as they are.
