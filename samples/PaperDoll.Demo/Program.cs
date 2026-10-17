// PaperDoll demo. Usage: PaperDoll.Demo [puppet files...] [--test-scene] [--screenshot <file.png>] [--frames <n>] [--no-atlas]
//                       [--no-physics] [--texture-scale <1|0.5|0.25|0.125>]
//                       [--set <name>=<x>[,<y>]]... [--play <animation>]... [--bench [<frames>]]
// Opens the given puppets (up to five); more can be added from .samples/ or a file dialog.
// --screenshot draws a few frames, saves the window to a PNG and exits (a smoke test that needs no one at the keyboard);
// --frames sets how many (10 by default; at 60 frames a second, 180 lets physics settle).
// --no-atlas starts with the texture atlas off (each texture uploaded on its own); it can be switched in the Puppets window.
// --no-physics starts every puppet with physics off (it can be switched per puppet).
// --texture-scale uploads textures at a lower resolution (it can be switched in the Puppets window).
// --test-scene opens the generated blend test scene (every blend mode, masks and Composite layers).
// --set sets a parameter on every opened puppet that has one by that name, for posed screenshots.
// --play loops an animation on every opened puppet that has one by that name.
// --bench loops every animation, sweeps every parameter physics does not drive, measures PaperDoll's update and draw
// over 600 frames (or <frames>) after a warm-up, prints the results and exits.

using PaperDoll.Demo;

var files = new List<string>();
string? screenshot = null;
var frames = 10;
var atlas = true;
var physics = true;
var settings = new List<(string Name, System.Numerics.Vector2 Value)>();
var animations = new List<string>();
var bench = 0;
var textureScale = 1f;
for (var i = 0; i < args.Length; i++)
{
    if (args[i] == "--screenshot" && i + 1 < args.Length)
        screenshot = args[++i];
    else if (args[i] == "--frames" && i + 1 < args.Length && int.TryParse(args[i + 1], out var count) && count > 0)
        (frames, i) = (count, i + 1);
    else if (args[i] == "--test-scene")
        files.Add(TestScene.Name);
    else if (args[i] == "--no-physics")
        physics = false;
    else if (args[i] == "--texture-scale" && i + 1 < args.Length
             && float.TryParse(args[i + 1], System.Globalization.CultureInfo.InvariantCulture, out var scale) && scale is 1f or 0.5f or 0.25f or 0.125f)
        (textureScale, i) = (scale, i + 1);
    else if (args[i] == "--no-atlas")
        atlas = false;
    else if (args[i] == "--set" && i + 1 < args.Length && DemoGame.TryParseSetting(args[++i], out var setting))
        settings.Add(setting);
    else if (args[i] == "--bench" && i + 1 < args.Length && int.TryParse(args[i + 1], out var benchFrames) && benchFrames > 0)
        (bench, i) = (benchFrames, i + 1);
    else if (args[i] == "--bench")
        bench = 600;
    else if (args[i] == "--play" && i + 1 < args.Length)
        animations.Add(args[++i]);
    else
        files.Add(args[i]);
}

using var game = new DemoGame([.. files]) { ScreenshotPath = screenshot, ScreenshotFrames = frames, StartPhysics = physics, UseAtlas = atlas, StartupSettings = settings,
    StartupAnimations = animations, BenchFrames = bench, TextureScale = textureScale };
game.Run();
