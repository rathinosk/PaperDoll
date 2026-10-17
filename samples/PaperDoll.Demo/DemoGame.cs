using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using ImGuiNET;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using PaperDoll.Snapshots;
#if MONOGAME
using MonoGame.Framework.Utilities;
using PaperDoll.MonoGame;
#else
using PaperDoll.Fna;
using SDL3;
#endif

namespace PaperDoll.Demo;

/// <summary>
/// The PaperDoll demo: up to five puppets on screen, each its own instance with its own placement, drawn through
/// PaperDoll.FNA (or PaperDoll.MonoGame, built with MONOGAME), and an ImGui debug interface.
/// </summary>
internal sealed partial class DemoGame : Game
{
    /// <summary>NinetyNineDays shows up to five puppets at once.</summary>
    public const int MaxSlots = 5;

    private const float MinScale = 0.02f;
    private const float MaxScale = 4f;

#if !MONOGAME
    private static readonly IntPtr FilterName = Marshal.StringToCoTaskMemUTF8("Inochi2D puppets");
    private static readonly IntPtr FilterPattern = Marshal.StringToCoTaskMemUTF8("inp;inx");

#endif
    private readonly GraphicsDeviceManager _graphics;
    private readonly string[] _startupFiles;
    private readonly List<PuppetSlot> _slots = [];
    private readonly ConcurrentQueue<string> _openedFiles = new();
#if !MONOGAME
    private readonly SDL.SDL_DialogFileCallback _dialogCallback;
#endif

    private ImGuiRenderer _imgui = null!;
    private SpriteBatch _spriteBatch = null!;
    private Texture2D _pixel = null!;
    private PuppetRenderer _renderer = null!;
    private PuppetLibrary _library = null!;
    private SnapshotStore? _store;
    private string? _samplesRoot;
    private IReadOnlyList<string> _samplePuppets = [];

    private PuppetSlot? _selected;
    private PuppetSlot? _dragging;
    private System.Numerics.Vector2 _dragOffset;
    private MouseState _previousMouse;
    private string? _status;
    private bool _statusIsError;
    private int _framesDrawn;

    // Rendering options and statistics, shown in the Puppets window.
    private bool _advancedBlending = true;
    private bool _showBounds;
    private int _drawCalls;
    private int _itemsDrawn;
    private int _layersDrawn;
    private double _drawMilliseconds;
    private readonly System.Diagnostics.Stopwatch _drawClock = new();

    public DemoGame(string[] startupFiles)
    {
        _startupFiles = startupFiles;
#if !MONOGAME
        _dialogCallback = OnDialogFiles;
#endif
        _graphics = new GraphicsDeviceManager(this)
        {
            GraphicsProfile = GraphicsProfile.HiDef, // PuppetRenderer needs it; MonoGame defaults to Reach
            PreferredBackBufferWidth = 1600,
            PreferredBackBufferHeight = 900,
            SynchronizeWithVerticalRetrace = true,
        };

        // PuppetRenderer draws each puppet through offscreen layers, so the back buffer must keep what is already drawn.
        _graphics.PreparingDeviceSettings += (_, e) =>
            e.GraphicsDeviceInformation.PresentationParameters.RenderTargetUsage = RenderTargetUsage.PreserveContents;
        IsMouseVisible = true;
        Window.AllowUserResizing = true;
        Window.ClientSizeChanged += OnClientSizeChanged;
#if MONOGAME
        Window.Title = $"PaperDoll Demo (MonoGame, {PlatformInfo.GraphicsBackend})";
        // SDL2 (DesktopGL) has no file dialog: puppet files are dropped on the window instead.
        Window.FileDrop += (_, e) =>
        {
            foreach (var file in e.Files)
                _openedFiles.Enqueue(file);
        };
#else
        Window.Title = "PaperDoll Demo";
#endif
    }

    /// <summary>If set, the demo saves the window to this PNG after a few frames and exits.</summary>
    public string? ScreenshotPath { get; init; }

    /// <summary>Whether puppets start with physics on.</summary>
    public bool StartPhysics { get; init; } = true;

    /// <summary>How many frames to draw before the screenshot.</summary>
    public int ScreenshotFrames { get; init; } = 10;

    /// <summary>Whether puppets start with their textures packed into atlas pages.</summary>
    public bool UseAtlas { get; init; } = true;

    /// <summary>The resolution puppet textures are uploaded at: 1, 0.5, 0.25 or 0.125.</summary>
    public float TextureScale { get; init; } = 1f;

    /// <summary>Parameter values (by name) set on every startup puppet that has the parameter.</summary>
    public IReadOnlyList<(string Name, System.Numerics.Vector2 Value)> StartupSettings { get; init; } = [];

    /// <summary>Animations (by name) looped on every startup puppet that has them.</summary>
    public IReadOnlyList<string> StartupAnimations { get; init; } = [];

    /// <summary>Parses <c>name=x</c> or <c>name=x,y</c> (invariant culture).</summary>
    public static bool TryParseSetting(string text, out (string Name, System.Numerics.Vector2 Value) setting)
    {
        setting = default;
        var equals = text.LastIndexOf('=');
        if (equals <= 0)
            return false;
        var parts = text[(equals + 1)..].Split(',');
        var style = System.Globalization.NumberStyles.Float;
        var culture = System.Globalization.CultureInfo.InvariantCulture;
        if (!float.TryParse(parts[0], style, culture, out var x))
            return false;
        var y = 0f;
        if (parts.Length > 1 && !float.TryParse(parts[1], style, culture, out y))
            return false;
        setting = (text[..equals], new System.Numerics.Vector2(x, y));
        return true;
    }

    protected override void Initialize()
    {
        base.Initialize();

        // Window layout is kept beside the executable; screenshots always start from the default layout.
        _imgui = new ImGuiRenderer(this, ScreenshotPath is null ? Path.Combine(AppContext.BaseDirectory, "imgui.ini") : null);
        _spriteBatch = new SpriteBatch(GraphicsDevice);
        _pixel = new Texture2D(GraphicsDevice, 1, 1);
        _pixel.SetData([Color.White]);
        _renderer = new PuppetRenderer(GraphicsDevice);

        _samplesRoot = SampleLocator.FindSamplesRoot();
        _store = _samplesRoot is null ? null : new SnapshotStore(_samplesRoot);
        _samplePuppets = _samplesRoot is null
            ? []
            : SampleLocator.FindPuppets(_samplesRoot).Select(p => Path.GetRelativePath(_samplesRoot, p).Replace('\\', '/')).ToList();
        _library = new PuppetLibrary(GraphicsDevice, _store) { UseAtlas = UseAtlas, TextureScale = TextureScale };

        foreach (var file in _startupFiles)
            AddPuppet(file);
        foreach (var slot in _slots)
        {
            if (!StartPhysics)
            {
                // Physics ran once when the instance was created; start from the plain defaults instead.
                slot.Instance.PhysicsEnabled = false;
                slot.Instance.ResetParameters();
            }

            foreach (var (name, value) in StartupSettings)
            {
                if (slot.Instance.Puppet.FindParameter(name) is { } parameter)
                    slot.Instance.SetParameterValue(parameter, value);
            }

            foreach (var name in StartupAnimations)
            {
                if (slot.Instance.TryGetAnimation(name, out var playback))
                    playback.Play(loop: true);
            }
        }

        if (Benchmarking)
            StartBench();
    }

    protected override void UnloadContent()
    {
        _library.Dispose();
        _renderer.Dispose();
        _imgui.Dispose();
        _pixel.Dispose();
        _spriteBatch.Dispose();
        base.UnloadContent();
    }

    protected override void Update(GameTime gameTime)
    {
        while (_openedFiles.TryDequeue(out var file))
            AddPuppet(file);

        UpdateSceneInput();

        // Each instance has its own clock (docs/design/runtime-model.md); the demo happens to step them all by the frame time.
        var delta = (float)gameTime.ElapsedGameTime.TotalSeconds;
        BenchBeginSection();
        foreach (var slot in _slots)
        {
            if (Benchmarking)
                SweepParameters(slot);
            slot.FeedScenePosition();
            var started = System.Diagnostics.Stopwatch.GetTimestamp();
            slot.Instance.Update(delta);
            slot.UpdateMilliseconds += System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        }

        BenchEndSection(_benchUpdate, ref _benchAllocatedUpdate);

        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(new Color(38, 40, 46));

        BenchBeginSection();
        DrawPuppets();
        BenchEndSection(_benchDraw, ref _benchAllocatedDraw);

        _spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.NonPremultiplied, SamplerState.LinearClamp, null, null);
        foreach (var slot in _slots)
        {
            if (_showBounds || slot == _selected)
                DrawOutline(slot.Bounds, slot == _selected ? new Color(255, 196, 64, 160) : new Color(150, 160, 180, 160), 1);
        }

        _spriteBatch.End();

        _imgui.BeforeLayout(gameTime);
        DrawLabels();
        DrawUi();
        _imgui.AfterLayout();

        // ImGui lays windows out over the first frames, so wait a little before the screenshot.
        if (ScreenshotPath is not null && ++_framesDrawn == ScreenshotFrames)
        {
            SaveScreenshot(ScreenshotPath);
            Exit();
        }

        BenchFrameDone();

        base.Draw(gameTime);
    }

    private void SaveScreenshot(string path)
    {
        var width = GraphicsDevice.PresentationParameters.BackBufferWidth;
        var height = GraphicsDevice.PresentationParameters.BackBufferHeight;
        var pixels = new Color[width * height];
        GraphicsDevice.GetBackBufferData(pixels);
        using var texture = new Texture2D(GraphicsDevice, width, height);
        texture.SetData(pixels);
        using var stream = File.Create(path);
        texture.SaveAsPng(stream, width, height);
        Console.WriteLine($"Saved {Path.GetFullPath(path)}");
    }

    /// <summary>Opens a puppet file in a new slot, reusing the loaded puppet if another slot already shows it.</summary>
    private void AddPuppet(string path)
    {
        if (_slots.Count >= MaxSlots)
        {
            SetStatus($"Up to {MaxSlots} puppets at once; remove one first.", error: true);
            return;
        }

        LoadedPuppet loaded;
        try
        {
            loaded = _library.Acquire(path);
        }
        catch (Exception ex) when (ex is PuppetFormatException or IOException or UnauthorizedAccessException)
        {
            SetStatus($"{Path.GetFileName(path)}: {ex.Message}", error: true);
            return;
        }

        var copy = 1;
        while (_slots.Any(s => s.Source == loaded && s.Copy == copy))
            copy++;

        var viewport = GraphicsDevice.Viewport;
        var slot = new PuppetSlot(loaded, copy) { Position = NextPosition() };
        slot.Scale = Math.Clamp(slot.FitScale(viewport.Width / (float)MaxSlots * 0.95f, viewport.Height * 0.85f), MinScale, MaxScale);
        if (path == TestScene.Name)
        {
            // The test scene is a page of cells rather than a character: give it most of the window.
            slot.Position = new System.Numerics.Vector2(viewport.Width / 2f, viewport.Height / 2f);
            slot.Scale = Math.Clamp(slot.FitScale(viewport.Width * 0.6f, viewport.Height * 0.9f), MinScale, MaxScale);
        }
        _slots.Add(slot);
        _selected = slot;
        SetStatus($"Opened {slot.Label}: {loaded.Puppet.Nodes.Count} nodes, {loaded.Puppet.Parameters.Count} parameters, {loaded.Puppet.Issues.Count} load notes.", error: false);
    }

    private void RemoveSlot(PuppetSlot slot)
    {
        _slots.Remove(slot);
        if (_selected == slot)
            _selected = _slots.LastOrDefault();
        if (_dragging == slot)
            _dragging = null;
        _library.Release(slot.Source);
        _imgui.ReleaseDisposedTextures();
    }

    /// <summary>Spreads puppets across the window: the first in the middle, the rest at the centres of the free fifths.</summary>
    private System.Numerics.Vector2 NextPosition()
    {
        var viewport = GraphicsDevice.Viewport;
        if (_slots.Count == 0)
            return new(viewport.Width / 2f, viewport.Height / 2f); // a puppet on its own goes in the middle (the centre fifth)
        for (var i = 0; i < MaxSlots; i++)
        {
            var candidate = new System.Numerics.Vector2(viewport.Width * (i + 0.5f) / MaxSlots, viewport.Height * 0.5f);
            if (!_slots.Any(s => System.Numerics.Vector2.Distance(s.Position, candidate) < 20))
                return candidate;
        }

        return new(viewport.Width / 2f, viewport.Height / 2f);
    }

    /// <summary>Left-drag moves a puppet, the wheel scales the one under the cursor; ignored while ImGui has the mouse.</summary>
    private void UpdateSceneInput()
    {
        var mouse = Mouse.GetState();
        var position = new System.Numerics.Vector2(mouse.X, mouse.Y);
        var pressed = mouse.LeftButton == ButtonState.Pressed;
        var wasPressed = _previousMouse.LeftButton == ButtonState.Pressed;
        var wheel = mouse.ScrollWheelValue - _previousMouse.ScrollWheelValue;
        _previousMouse = mouse;

        if (!IsActive || ImGui.GetIO().WantCaptureMouse)
        {
            _dragging = null;
            return;
        }

        var hovered = SlotAt(mouse.X, mouse.Y);
        if (pressed && !wasPressed && hovered is not null)
        {
            _selected = hovered;
            _dragging = hovered;
            _dragOffset = hovered.Position - position;
            _slots.Remove(hovered);
            _slots.Add(hovered); // bring to front
        }
        else if (!pressed)
        {
            _dragging = null;
        }

        if (_dragging is not null)
            _dragging.Position = position + _dragOffset;

        if (wheel != 0 && hovered is not null)
            hovered.Scale = Math.Clamp(hovered.Scale * MathF.Pow(1.1f, wheel / 120f), MinScale, MaxScale);
    }

    private PuppetSlot? SlotAt(int x, int y)
    {
        for (var i = _slots.Count - 1; i >= 0; i--)
        {
            if (_slots[i].Bounds.Contains(x, y))
                return _slots[i];
        }

        return null;
    }

    /// <summary>Draws every slot's instance, back to front in slot order, and records the frame's statistics.</summary>
    private void DrawPuppets()
    {
        _renderer.AdvancedBlending = _advancedBlending;
        _drawCalls = 0;
        _itemsDrawn = 0;
        _layersDrawn = 0;
        _drawClock.Restart();
        foreach (var slot in _slots)
        {
            _renderer.Draw(slot.Instance, slot.Source.Textures, slot.Transform);
            slot.LastDrawCalls = _renderer.LastDrawCalls;
            _drawCalls += _renderer.LastDrawCalls;
            _layersDrawn += _renderer.LastLayers;
            var list = slot.Instance.DrawList;
            for (var i = 0; i < list.Count; i++)
            {
                if (list[i].Texture is not null)
                    _itemsDrawn++;
            }
        }

        _drawMilliseconds = _drawClock.Elapsed.TotalMilliseconds;
    }

    private void DrawOutline(Rectangle r, Color color, int width)
    {
        _spriteBatch.Draw(_pixel, new Rectangle(r.Left, r.Top, r.Width, width), color);
        _spriteBatch.Draw(_pixel, new Rectangle(r.Left, r.Bottom - width, r.Width, width), color);
        _spriteBatch.Draw(_pixel, new Rectangle(r.Left, r.Top, width, r.Height), color);
        _spriteBatch.Draw(_pixel, new Rectangle(r.Right - width, r.Top, width, r.Height), color);
    }

    /// <summary>Puppet names under their frames, drawn with ImGui's font since the demo has no content pipeline.</summary>
    private void DrawLabels()
    {
        var draw = ImGui.GetBackgroundDrawList();
        foreach (var slot in _slots)
        {
            var bounds = slot.Bounds;
            var text = slot.Label;
            var size = ImGui.CalcTextSize(text);
            draw.AddText(new System.Numerics.Vector2(bounds.Center.X - size.X / 2, bounds.Bottom + 4),
                slot == _selected ? 0xFF40C4FFu : 0xFFE0E0E0u, text);

            var transform = slot.Transform;
            foreach (var (label, position) in slot.Source.Labels)
            {
                var at = Vector2.Transform(new Vector2(position.X, position.Y), transform);
                var labelSize = ImGui.CalcTextSize(label);
                draw.AddText(new System.Numerics.Vector2(at.X - labelSize.X / 2, at.Y - labelSize.Y - 2), 0xFFE0E0E0u, label);
            }
        }
    }

    private void SetStatus(string message, bool error)
    {
        _status = message;
        _statusIsError = error;
        if (error)
            Console.Error.WriteLine(message);
    }

#if !MONOGAME
    private void ShowOpenDialog()
    {
        var filters = new[] { new SDL.SDL_DialogFileFilter() };
        unsafe
        {
            filters[0].name = (byte*)FilterName;
            filters[0].pattern = (byte*)FilterPattern;
        }

        SDL.SDL_ShowOpenFileDialog(_dialogCallback, IntPtr.Zero, Window.Handle, filters, filters.Length, _samplesRoot!, true);
    }

    /// <summary>SDL may call this on another thread, so the files are queued for <see cref="Update"/>.</summary>
    private void OnDialogFiles(IntPtr userdata, IntPtr fileList, int filter)
    {
        if (fileList == IntPtr.Zero)
        {
            SetStatus($"File dialog failed: {SDL.SDL_GetError()}", error: true);
            return;
        }

        for (var i = 0; ; i++)
        {
            var entry = Marshal.ReadIntPtr(fileList, i * IntPtr.Size);
            if (entry == IntPtr.Zero)
                break;
            if (Marshal.PtrToStringUTF8(entry) is { } file)
                _openedFiles.Enqueue(file);
        }
    }
#endif

    private void OnClientSizeChanged(object? sender, EventArgs e)
    {
        var bounds = Window.ClientBounds;
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return;
        _graphics.PreferredBackBufferWidth = bounds.Width;
        _graphics.PreferredBackBufferHeight = bounds.Height;
        _graphics.ApplyChanges();
    }
}
