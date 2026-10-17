// Draws a PuppetInstance's draw list through FNA or MonoGame: each puppet and each Composite in an offscreen layer sized
// to what it covers on screen, masks through the layer's stencil buffer, fixed-function and shader blend modes, and runs
// of Parts on one atlas page with the same blending and masks batched into one draw call.
// Compiled into PaperDoll.FNA and PaperDoll.MonoGame; MONOGAME marks where they differ.

using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#if MONOGAME
using MonoGame.Framework.Utilities;
#endif

#if MONOGAME
namespace PaperDoll.MonoGame;
#else
namespace PaperDoll.Fna;
#endif

/// <summary>
/// Draws puppet instances. One renderer can draw any number of instances of any puppets; it keeps no per-puppet state.
/// Consecutive Parts on one GPU texture (an atlas page) with the same fixed-function blending and the same masks are
/// drawn in one call.
/// </summary>
/// <remarks>
/// <para>
/// Each instance is drawn into a layer of its own, cleared to transparent, and that layer is then drawn
/// onto the current render target with premultiplied blending. So a puppet's blend modes and masks act only on the
/// puppet itself: one puppet's masks never clip another's, and a <see cref="BlendMode.ClipToLower"/> Part clips to the
/// puppet below it, not to the scene. Each <see cref="Composite"/> is drawn into a layer of its own too, which is then
/// drawn like a Part with the Composite's opacity, tints, blend mode and masks. A layer is cleared, drawn and copied only
/// over the viewport pixels its contents cover, so its cost follows the puppet's size on screen; a layer entirely outside
/// the viewport is not drawn at all. The renderer keeps one render target per nesting depth, grown as needed (at most the
/// viewport's size, rounded up), with a stencil buffer only at depths where something has been masked.
/// </para>
/// <para>
/// Masks use the layer's stencil buffer. The blend modes that need advanced blend equations (Multiply, Screen,
/// Overlay, Darken, Lighten, ColorDodge, ColorBurn, HardLight, SoftLight, Difference, Exclusion) are drawn in a shader
/// that reads a copy of the layer below the drawable, with the equations of OpenGL's KHR_blend_equation_advanced; with
/// <see cref="AdvancedBlending"/> off they use the fixed-function fallback instead (see <see cref="AdvancedBlending"/>). Every other mode is
/// fixed-function blending.
/// </para>
/// <para>
/// <see cref="Draw"/> switches render targets and restores the current one (and its viewport) afterwards, so the current
/// target must keep its contents: a <see cref="RenderTarget2D"/> created with <see cref="RenderTargetUsage.PreserveContents"/>,
/// or the back buffer with <see cref="PresentationParameters.RenderTargetUsage"/> set to
/// <see cref="RenderTargetUsage.PreserveContents"/>. It leaves the device's blend, depth-stencil, rasterizer and first
/// two sampler states set, as <see cref="SpriteBatch"/> does (and on MonoGame's OpenGL backend, its vertex and index
/// buffers). Drawing allocates no managed memory once the renderer's
/// buffers and layers have grown to the puppets it draws.
/// </para>
/// </remarks>
public sealed class PuppetRenderer : IDisposable
{
#if !MONOGAME
    private const string EffectResource = "PaperDoll.Fna.Shaders.PaperDoll.fxb";
#endif
    private const float PixelShift = 0.25f;

    /// <summary>Layer render targets grow in steps of this many pixels, so a puppet growing on screen does not recreate them every frame.</summary>
    private const int LayerAlignment = 128;

    /// <summary>The largest stencil value; after it, a layer's stencil is cleared and its values start again.</summary>
    private const int MaxStencilValue = 255;

    // The stencil states a mask pass uses, one per stencil value (FNA takes the reference value from the state), made on first use.
    private static readonly DepthStencilState StencilZero = StencilState(CompareFunction.Always, StencilOperation.Zero, 0);
    private readonly DepthStencilState?[] _stencilReplace = new DepthStencilState?[MaxStencilValue + 1];
    private readonly DepthStencilState?[] _stencilEqual = new DepthStencilState?[MaxStencilValue + 1];
    private readonly DepthStencilState?[] _stencilNotEqual = new DepthStencilState?[MaxStencilValue + 1];

    private static readonly Vector4 WholeTexture = new(0, 0, 1, 1);

    private readonly GraphicsDevice _device;
    private readonly Effect _effect;
    private readonly EffectParameter _transform;
    private readonly EffectParameter _pixelOffset;
    private readonly EffectParameter _backdropScale;
    private readonly EffectParameter _albedo;
    private readonly EffectParameter _backdropTexture;
    private readonly EffectTechnique _partTechnique;
    private readonly EffectTechnique _maskTechnique;
    private readonly EffectTechnique?[] _advancedTechniques;
    private readonly Texture2D _white;
    private readonly TextureRegion _whiteRegion;
    private readonly List<LayerTarget?> _targets = []; // per nesting depth
    private readonly RenderTargetBinding[] _hostTargets = new RenderTargetBinding[8];
    private Layer[] _layers = new Layer[8];
    private LayerPlan[] _plans = [];
    private int[] _open = new int[8];
    private RenderTarget2D? _backdrop;
    private PuppetVertex[] _vertices = new PuppetVertex[4096];
    private int[] _indices = new int[8192];
    private int _vertexCount;
    private int _indexCount;
    private Texture2D? _batchTexture;
    private BlendState? _batchBlend;
    private bool _batchMasked;
    private IReadOnlyList<DrawMask>? _stencilMasks; // the masks the current layer's stencil holds, if known
    private DepthStencilState _maskTest = DepthStencilState.None; // how items under _stencilMasks test the stencil
    private DepthStencilState _maskWrite = DepthStencilState.None; // how the current pass's sources write it
    private int[] _stencilValues = new int[8];         // per depth: the last stencil value its layer used
#if MONOGAME

    // MonoGame's OpenGL backend draws user primitives from client memory, which costs the driver an index scan and a copy
    // on every call (about 20 µs); there, batches are streamed through dynamic buffers instead.
    private const int StreamVertices = 1 << 16;
    private const int StreamIndices = 1 << 17;
    private readonly bool _streamBatches;
    private DynamicVertexBuffer? _streamVertexBuffer;
    private DynamicIndexBuffer? _streamIndexBuffer;
    private int _streamVertexCursor;
    private int _streamIndexCursor;
#endif

    // Per Draw call.
    private Matrix _puppetToPixels;
    private Matrix _puppetToClip;
    private Matrix _pixelsToClip;
    private int _width;
    private int _height;
    private int _depth;
    private SamplerState _sampler = SamplerState.LinearClamp;

#if MONOGAME
    /// <summary>Creates a renderer for a graphics device.</summary>
    /// <exception cref="NotSupportedException">MonoGame runs on a backend other than OpenGL (DesktopGL) or DirectX 11 (WindowsDX).</exception>
#else
    /// <summary>Creates a renderer for a graphics device.</summary>
#endif
    public PuppetRenderer(GraphicsDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        _device = device;
        _effect = new Effect(device, LoadEffectCode());
        _transform = _effect.Parameters["Transform"];
        _pixelOffset = _effect.Parameters["PixelOffset"];
        _backdropScale = _effect.Parameters["BackdropScale"];
        _albedo = _effect.Parameters["Albedo"];
        _backdropTexture = _effect.Parameters["Backdrop"];
        _partTechnique = _effect.Techniques["Part"];
        _maskTechnique = _effect.Techniques["Mask"];
        _advancedTechniques = new EffectTechnique?[Enum.GetValues<BlendMode>().Length];
        foreach (var mode in Enum.GetValues<BlendMode>())
        {
            if (BlendModes.IsAdvanced(mode))
                _advancedTechniques[(int)mode] = _effect.Techniques[mode.ToString()];
        }

#if MONOGAME
        _streamBatches = PlatformInfo.GraphicsBackend == GraphicsBackend.OpenGL;
#endif
        _white = new Texture2D(device, 1, 1);
        _white.SetData([Color.White]);
        _whiteRegion = new TextureRegion(_white, Vector2.Zero, Vector2.Zero, Vector2.Zero, Vector2.One);
    }

    /// <summary>
    /// Whether the advanced blend modes (see the remarks on this class) are drawn with their real equations (the default),
    /// rather than with the fixed-function fallback nijilive uses on GPUs without advanced blending, which draws several of
    /// them as Normal.
    /// </summary>
    public bool AdvancedBlending { get; set; } = true;

    /// <summary>Draw calls made by the last <see cref="Draw"/>, including mask sources, layers and copies.</summary>
    public int LastDrawCalls { get; private set; }

    /// <summary>Offscreen layers drawn by the last <see cref="Draw"/>: the puppet's own and one per Composite.</summary>
    public int LastLayers { get; private set; }

    /// <summary>
    /// Draws an instance's draw list (as of its last update) into the current render target, within its viewport.
    /// </summary>
    /// <param name="instance">The instance to draw.</param>
    /// <param name="textures">The textures of the instance's puppet.</param>
    /// <param name="transform">Maps puppet space to viewport pixels (x right, y down): the camera and placement.</param>
    /// <param name="filter">If set, only Parts it accepts are drawn (Composites are always drawn).</param>
    /// <exception cref="InvalidOperationException">The current render target does not preserve its contents.</exception>
    public void Draw(PuppetInstance instance, PuppetTextures textures, Matrix transform, Predicate<DrawItem>? filter = null)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(textures);
        if (!ReferenceEquals(instance.Puppet, textures.Puppet))
            throw new ArgumentException("The textures belong to a different puppet.", nameof(textures));

#if MONOGAME
        var hostCount = _device.RenderTargetCount;
        _device.GetRenderTargets(_hostTargets); // copies the bound targets to the start of the array
#else
        var hostCount = _device.GetRenderTargetsNoAllocEXT(_hostTargets);
#endif
        RequirePreservedTarget(hostCount);
        var hostViewport = _device.Viewport;
        var hostScissor = _device.ScissorRectangle;

        LastDrawCalls = 0;
        LastLayers = 0;
        _width = hostViewport.Width;
        _height = hostViewport.Height;
        _sampler = textures.Sampler;
        _puppetToPixels = transform;

        var list = instance.DrawList;
        PlanLayers(list, textures, filter);
        var puppetRect = PixelRect(_plans[list.Count].Bounds);
        if (puppetRect.Width <= 0 || puppetRect.Height <= 0)
        {
            Array.Clear(_hostTargets);
            return; // nothing drawn, or all of it outside the viewport
        }

        _depth = 0;
        BeginLayer(puppetRect, _plans[list.Count].Stencil);
        _device.RasterizerState = RasterizerState.CullNone;
        var skipping = 0; // Composite layers deep inside one that is not drawn
        for (var i = 0; i < list.Count; i++)
        {
            var item = list[i];
            if (skipping > 0)
            {
                skipping += item.Kind switch { DrawItemKind.BeginComposite => 1, DrawItemKind.EndComposite => -1, _ => 0 };
                continue;
            }

            switch (item.Kind)
            {
                case DrawItemKind.Part:
                    DrawPart(item, textures, filter);
                    break;
                case DrawItemKind.BeginComposite:
                    Flush();
                    var rect = PixelRect(_plans[i].Bounds);
                    if (rect.Width <= 0 || rect.Height <= 0)
                    {
                        skipping = 1; // empty, or outside the viewport: up to its end, nothing it holds can show
                        break;
                    }

                    _depth++;
                    BeginLayer(rect, _plans[i].Stencil);
                    break;
                case DrawItemKind.EndComposite:
                    Flush();
                    var layer = _layers[_depth];
                    _depth--;
                    SwitchTo(_layers[_depth]);
                    if (item.Opacity > 0)
                        DrawLayer(layer, item, textures);
                    break;
            }
        }

        Flush();
        var puppet = _layers[0];
        RestoreHost(hostCount, hostViewport, hostScissor);

        // The puppet's layer onto the host's target: premultiplied, pixel for pixel.
        _pixelsToClip = Matrix.CreateTranslation(-PixelShift, -PixelShift, 0) * Matrix.CreateOrthographicOffCenter(0, _width, _height, 0, 0, 1);
        _device.BlendState = BlendState.AlphaBlend;
        _device.DepthStencilState = DepthStencilState.None;
        DrawQuad(puppet, puppet.Rect, Color.White, Color.Transparent, _partTechnique);
    }

    /// <summary>Whether a Part item draws anything: it has a texture on the GPU, some opacity and triangles, and the filter takes it.</summary>
    private static bool Draws(DrawItem item, PuppetTextures textures, Predicate<DrawItem>? filter) =>
        item.Texture is { } slot && item.Opacity > 0 && !item.Indices.IsEmpty && filter?.Invoke(item) != false
        && textures.Get(slot) is not null;

    /// <summary>
    /// Works out, before drawing, what each layer holds: the puppet-space rectangle around what is drawn in it and whether
    /// anything drawn in it is masked. The plan of a Composite's layer is kept at its <see cref="DrawItemKind.BeginComposite"/>
    /// item's index, the puppet's own at <c>list.Count</c>.
    /// </summary>
    private void PlanLayers(IReadOnlyList<DrawItem> list, PuppetTextures textures, Predicate<DrawItem>? filter)
    {
        if (_plans.Length < list.Count + 1)
            _plans = new LayerPlan[Math.Max(list.Count + 1, _plans.Length * 2)];

        var top = 0;
        _open[0] = list.Count;
        _plans[list.Count] = LayerPlan.Empty;
        for (var i = 0; i < list.Count; i++)
        {
            var item = list[i];
            switch (item.Kind)
            {
                case DrawItemKind.Part:
                    if (Draws(item, textures, filter))
                    {
                        ref var plan = ref _plans[_open[top]];
                        plan = new LayerPlan(plan.Bounds.Include(item.Positions), plan.Stencil || item.Masks.Count > 0);
                    }

                    break;
                case DrawItemKind.BeginComposite:
                    if (++top == _open.Length)
                        Array.Resize(ref _open, _open.Length * 2);
                    _open[top] = i;
                    _plans[i] = LayerPlan.Empty;
                    break;
                case DrawItemKind.EndComposite:
                    var child = _plans[_open[top]];
                    if (top > 0)
                        top--;
                    if (!child.Bounds.IsEmpty && item.Opacity > 0)
                    {
                        ref var parent = ref _plans[_open[top]];
                        parent = new LayerPlan(parent.Bounds.Include(child.Bounds), parent.Stencil || item.Masks.Count > 0);
                    }

                    break;
            }
        }
    }

    private void RequirePreservedTarget(int hostCount)
    {
        var usage = hostCount == 0
            ? _device.PresentationParameters.RenderTargetUsage
            : (_hostTargets[0].RenderTarget as RenderTarget2D)?.RenderTargetUsage ?? RenderTargetUsage.PreserveContents;
        if (usage == RenderTargetUsage.DiscardContents)
        {
            Array.Clear(_hostTargets);
            throw new InvalidOperationException(hostCount == 0
                ? "PuppetRenderer draws through offscreen layers and needs the back buffer to keep its contents: set PresentationParameters.RenderTargetUsage to PreserveContents."
                : "PuppetRenderer draws through offscreen layers and needs the current render target to keep its contents: create it with RenderTargetUsage.PreserveContents.");
        }
    }

    private void RestoreHost(int hostCount, Viewport viewport, Rectangle scissor)
    {
        if (hostCount == 0)
            _device.SetRenderTarget(null);
        else if (hostCount == 1 && _hostTargets[0].RenderTarget is RenderTarget2D target)
            _device.SetRenderTarget(target);
        else
            _device.SetRenderTargets(_hostTargets[..hostCount]);
        _device.Viewport = viewport;
        _device.ScissorRectangle = scissor;
        Array.Clear(_hostTargets);
    }

    /// <summary>
    /// Starts the layer at the current depth over <paramref name="rect"/> (viewport pixels): binds the depth's render
    /// target and clears the rectangle (and its stencil, if the layer masks anything) to transparent.
    /// </summary>
    private void BeginLayer(Rectangle rect, bool stencil)
    {
        if (_depth == _layers.Length)
        {
            Array.Resize(ref _layers, _layers.Length * 2);
            Array.Resize(ref _stencilValues, _layers.Length);
        }

        var layer = new Layer(TargetFor(rect.Width, rect.Height, stencil), rect);
        _layers[_depth] = layer;
        SwitchTo(layer);
        _device.BlendState = BlendState.Opaque;
        _device.DepthStencilState = stencil ? StencilZero : DepthStencilState.None;
        DrawRect(rect);
        _stencilValues[_depth] = 0;
        LastLayers++;
    }

    /// <summary>Binds a layer's render target and maps viewport pixels and puppet space onto it.</summary>
    private void SwitchTo(Layer layer)
    {
        _device.SetRenderTarget(layer.Target);
        _pixelsToClip = LayerTransform(layer, layer.Target.Width, layer.Target.Height);
        _puppetToClip = _puppetToPixels * _pixelsToClip;
        _stencilMasks = null;

        // Whether the stencil survives a change of render target is up to the backend: clear it before it is used again.
        _stencilValues[_depth] = MaxStencilValue;
    }

    /// <summary>
    /// Maps viewport pixels to the clip space of a target of the given size whose top-left pixel is the layer's. Layers
    /// are copied pixel for pixel with point sampling; a quarter-pixel shift puts every sample a quarter of a texel inside
    /// the right texel whether the backend puts pixel centres at whole coordinates (Direct3D 9) or at halves.
    /// </summary>
    private static Matrix LayerTransform(Layer layer, int width, int height) =>
        Matrix.CreateTranslation(-layer.Rect.X - PixelShift, -layer.Rect.Y - PixelShift, 0)
        * Matrix.CreateOrthographicOffCenter(0, width, height, 0, 0, 1);

    /// <summary>The current depth's render target, recreated larger (or with a stencil buffer) if it does not fit.</summary>
    private RenderTarget2D TargetFor(int width, int height, bool stencil)
    {
        while (_targets.Count <= _depth)
            _targets.Add(null);
        var current = _targets[_depth];
        if (current is { } kept && kept.Target.Width >= width && kept.Target.Height >= height && (kept.Stencil || !stencil))
            return kept.Target;

        var newWidth = Math.Max(AlignUp(width), current?.Target.Width ?? 0);
        var newHeight = Math.Max(AlignUp(height), current?.Target.Height ?? 0);
        var withStencil = stencil || current?.Stencil == true;
        current?.Target.Dispose();
        var target = new RenderTarget2D(_device, newWidth, newHeight, false, SurfaceFormat.Color,
            withStencil ? DepthFormat.Depth24Stencil8 : DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
        _targets[_depth] = new LayerTarget(target, withStencil);
        return target;
    }

    private static int AlignUp(int value) => (value + LayerAlignment - 1) / LayerAlignment * LayerAlignment;

    private void DrawPart(DrawItem item, PuppetTextures textures, Predicate<DrawItem>? filter)
    {
        if (item.Texture is not { } slot || item.Opacity <= 0 || item.Indices.IsEmpty || filter?.Invoke(item) == false)
            return;
        if (textures.Get(slot) is not { } region)
            return;

        var tint = item.Tint * item.Opacity;
        var color = new Color(tint.X, tint.Y, tint.Z, item.Opacity);
        var screen = new Color(item.ScreenTint.X, item.ScreenTint.Y, item.ScreenTint.Z, 0f);
        var advanced = AdvancedBlending && BlendModes.IsAdvanced(item.BlendMode);
        var masked = item.Masks.Count > 0;

        if (!advanced)
        {
            // Batched with the Parts before it if they share the texture, the blending and the masks.
            var blend = BlendModes.FixedFunctionState(item.BlendMode);
            if (masked && !SameMasks(_stencilMasks, item.Masks))
            {
                Flush();
                ApplyMasks(item.Masks, textures);
            }

            if (!ReferenceEquals(region.Texture, _batchTexture) || !ReferenceEquals(blend, _batchBlend) || masked != _batchMasked)
            {
                Flush();
                _batchTexture = region.Texture;
                _batchBlend = blend;
                _batchMasked = masked;
            }

            Append(item.Positions, item.Uvs, item.Indices, region, color, screen);
            return;
        }

        // Blended in the shader: drawn on its own, over a copy of what is below it.
        Flush();
        CopyBackdrop(PixelRect(LayerBounds.Empty.Include(item.Positions)));
        masked = ApplyMasks(item.Masks, textures);
        Append(item.Positions, item.Uvs, item.Indices, region, color, screen);
        DrawBatch(region.Texture, _sampler, _puppetToClip, _advancedTechniques[(int)item.BlendMode]!, BlendState.Opaque,
            masked ? _maskTest : DepthStencilState.None);
    }

    /// <summary>Draws a Composite's layer onto the one below like a Part, with the Composite's values.</summary>
    private void DrawLayer(Layer layer, DrawItem item, PuppetTextures textures)
    {
        var tint = item.Tint * item.Opacity;
        var color = new Color(tint.X, tint.Y, tint.Z, item.Opacity);
        var screen = new Color(item.ScreenTint.X, item.ScreenTint.Y, item.ScreenTint.Z, 0f);
        var advanced = AdvancedBlending && BlendModes.IsAdvanced(item.BlendMode);
        if (advanced)
            CopyBackdrop(layer.Rect);
        var masked = ApplyMasks(item.Masks, textures);
        _device.BlendState = advanced ? BlendState.Opaque : BlendModes.FixedFunctionState(item.BlendMode);
        _device.DepthStencilState = masked ? _maskTest : DepthStencilState.None;
        DrawQuad(layer, layer.Rect, color, screen, advanced ? _advancedTechniques[(int)item.BlendMode]! : _partTechnique);
        _device.DepthStencilState = DepthStencilState.None;
    }

    /// <summary>
    /// Copies the part of the current layer under <paramref name="rect"/> (viewport pixels) into the backdrop texture,
    /// which the advanced blend shaders read (a render target cannot be read while it is drawn into).
    /// </summary>
    private void CopyBackdrop(Rectangle rect)
    {
        var layer = _layers[_depth];
        var width = layer.Target.Width;
        var height = layer.Target.Height;
        if (_backdrop is null || _backdrop.Width < width || _backdrop.Height < height)
        {
            var backdropWidth = Math.Max(width, _backdrop?.Width ?? 0);
            var backdropHeight = Math.Max(height, _backdrop?.Height ?? 0);
            _backdrop?.Dispose();
            _backdrop = new RenderTarget2D(_device, backdropWidth, backdropHeight, false, SurfaceFormat.Color, DepthFormat.None, 0,
                RenderTargetUsage.PreserveContents);
        }

        // The copy sits at the same place in the backdrop as in the layer.
        _device.SetRenderTarget(_backdrop);
        _pixelsToClip = LayerTransform(layer, _backdrop.Width, _backdrop.Height);
        _device.BlendState = BlendState.Opaque;
        _device.DepthStencilState = DepthStencilState.None;
        DrawQuad(layer, rect, Color.White, Color.Transparent, _partTechnique);
        SwitchTo(layer);

        _backdropTexture.SetValue(_backdrop);
        _backdropScale.SetValue(new Vector2(width / (float)_backdrop.Width, height / (float)_backdrop.Height));
        _pixelOffset.SetValue(new Vector2(PixelShift / _backdrop.Width, PixelShift / _backdrop.Height));
        _device.SamplerStates[1] = SamplerState.PointClamp;
    }

    private static bool SameMasks(IReadOnlyList<DrawMask>? a, IReadOnlyList<DrawMask> b)
    {
        if (a is null || a.Count != b.Count)
            return false;
        if (ReferenceEquals(a, b))
            return true;
        for (var i = 0; i < a.Count; i++)
        {
            if (a[i] != b[i])
                return false;
        }

        return true;
    }

    /// <summary>
    /// Sets the stencil up for an item's masks. In principle: the stencil is cleared to 0 if any source is a <see cref="MaskingMode.Mask"/> (else to 1),
    /// then each source in turn writes 1 (Mask) or 0 (DodgeMask) where it counts; the item is drawn where the stencil is 1.
    /// Returns whether the item is masked.
    /// </summary>
    /// <remarks>
    /// The same is done without clearing: each pass takes a stencil value the layer has not used since its stencil was
    /// last cleared. Mask sources write it and the item draws where the stencil holds it; DodgeMask sources alone write
    /// it and the item draws where the stencil does not hold it. Only a mix of both modes (written in order) needs the stencil cleared, as does running out of values. Consecutive sources with the same mode on the same
    /// texture are drawn in one call.
    /// </remarks>
    private bool ApplyMasks(IReadOnlyList<DrawMask> masks, PuppetTextures textures)
    {
        if (masks.Count == 0)
            return false;
        if (SameMasks(_stencilMasks, masks))
            return true;

        bool anyMask = false, anyDodge = false;
        for (var i = 0; i < masks.Count; i++)
        {
            anyMask |= masks[i].Mode == MaskingMode.Mask;
            anyDodge |= masks[i].Mode == MaskingMode.DodgeMask;
        }

        ref var value = ref _stencilValues[_depth];
        if (value >= MaxStencilValue || (anyMask && anyDodge))
        {
            // Cleared to 0 over the layer's rectangle only (the rest of the target is never read).
            _device.BlendState = BlendModes.NoColour;
            _device.DepthStencilState = StencilZero;
            DrawRect(_layers[_depth].Rect);
            value = 0;
        }

        value++;
        _maskWrite = Cached(_stencilReplace, value, CompareFunction.Always, StencilOperation.Replace);
        _maskTest = anyMask
            ? Cached(_stencilEqual, value, CompareFunction.Equal, StencilOperation.Keep)
            : Cached(_stencilNotEqual, value, CompareFunction.NotEqual, StencilOperation.Keep);

        Texture2D? batchTexture = null;
        var batchMode = MaskingMode.Mask;
        for (var i = 0; i < masks.Count; i++)
        {
            var mask = masks[i];
            var source = mask.Source;
            TextureRegion region;
            if (source.IsShape)
                region = _whiteRegion;
            else if (source.Texture is { } slot && textures.Get(slot) is { } sourceRegion)
                region = sourceRegion;
            else
                continue; // a Part without a texture masks nothing

            if (batchTexture is not null && (!ReferenceEquals(region.Texture, batchTexture) || mask.Mode != batchMode))
                DrawMaskBatch(batchTexture, batchMode);
            batchTexture = region.Texture;
            batchMode = mask.Mode;
            var threshold = new Color(0f, 0f, 0f, source.Threshold);
            Append(source.Positions, source.Uvs, source.Indices, region, Color.White, threshold);
        }

        if (batchTexture is not null)
            DrawMaskBatch(batchTexture, batchMode);
        _stencilMasks = masks;
        return true;
    }

    /// <summary>Draws mask sources into the stencil: a DodgeMask source zeroes it when Mask sources are in the same pass.</summary>
    private void DrawMaskBatch(Texture2D texture, MaskingMode mode) =>
        DrawBatch(texture, _sampler, _puppetToClip, _maskTechnique, BlendModes.NoColour,
            mode == MaskingMode.DodgeMask && _maskTest.StencilFunction == CompareFunction.Equal ? StencilZero : _maskWrite);

    /// <summary>
    /// Draws <paramref name="layer"/>'s pixels over <paramref name="rect"/> (viewport pixels) pixel for pixel, with the
    /// current blend and depth-stencil states and <see cref="_pixelsToClip"/>.
    /// </summary>
    private void DrawQuad(Layer layer, Rectangle rect, Color color, Color screen, EffectTechnique technique)
    {
        var texture = layer.Target;
        float x0 = rect.Left, y0 = rect.Top, x1 = rect.Right, y1 = rect.Bottom;
        float w = texture.Width, h = texture.Height;
        float ox = layer.Rect.X, oy = layer.Rect.Y;
        _vertices[0] = new PuppetVertex(new Vector2(x0, y0), color, screen, new Vector2((x0 - ox) / w, (y0 - oy) / h), WholeTexture);
        _vertices[1] = new PuppetVertex(new Vector2(x1, y0), color, screen, new Vector2((x1 - ox) / w, (y0 - oy) / h), WholeTexture);
        _vertices[2] = new PuppetVertex(new Vector2(x1, y1), color, screen, new Vector2((x1 - ox) / w, (y1 - oy) / h), WholeTexture);
        _vertices[3] = new PuppetVertex(new Vector2(x0, y1), color, screen, new Vector2((x0 - ox) / w, (y1 - oy) / h), WholeTexture);
        _indices[0] = 0; _indices[1] = 1; _indices[2] = 2; _indices[3] = 0; _indices[4] = 2; _indices[5] = 3;
        _vertexCount = 4;
        _indexCount = 6;
        DrawBatch(texture, SamplerState.PointClamp, _pixelsToClip, technique, _device.BlendState, _device.DepthStencilState);
    }

    /// <summary>
    /// Fills <paramref name="rect"/> (viewport pixels) with transparent black through the current blend and depth-stencil
    /// states: a clear of just that rectangle, of colour, stencil or both.
    /// </summary>
    private void DrawRect(Rectangle rect)
    {
        float x0 = rect.Left, y0 = rect.Top, x1 = rect.Right, y1 = rect.Bottom;
        _vertices[0] = new PuppetVertex(new Vector2(x0, y0), Color.Transparent, Color.Transparent, Vector2.Zero, WholeTexture);
        _vertices[1] = new PuppetVertex(new Vector2(x1, y0), Color.Transparent, Color.Transparent, Vector2.Zero, WholeTexture);
        _vertices[2] = new PuppetVertex(new Vector2(x1, y1), Color.Transparent, Color.Transparent, Vector2.Zero, WholeTexture);
        _vertices[3] = new PuppetVertex(new Vector2(x0, y1), Color.Transparent, Color.Transparent, Vector2.Zero, WholeTexture);
        _indices[0] = 0; _indices[1] = 1; _indices[2] = 2; _indices[3] = 0; _indices[4] = 2; _indices[5] = 3;
        _vertexCount = 4;
        _indexCount = 6;
        DrawBatch(_white, SamplerState.PointClamp, _pixelsToClip, _partTechnique, _device.BlendState, _device.DepthStencilState);
    }

    private void Append(ReadOnlySpan<System.Numerics.Vector2> positions, ReadOnlySpan<System.Numerics.Vector2> uvs,
        ReadOnlySpan<ushort> indices, TextureRegion region, Color color, Color screen)
    {
        if (_vertexCount + positions.Length > _vertices.Length)
            Array.Resize(ref _vertices, Math.Max(_vertices.Length * 2, _vertexCount + positions.Length));
        if (_indexCount + indices.Length > _indices.Length)
            Array.Resize(ref _indices, Math.Max(_indices.Length * 2, _indexCount + indices.Length));

        var clip = new Vector4(region.ClipMin.X, region.ClipMin.Y, region.ClipMax.X, region.ClipMax.Y);
        var baseVertex = _vertexCount;
        for (var v = 0; v < positions.Length; v++)
        {
            var uv = v < uvs.Length ? uvs[v] : default;
            _vertices[_vertexCount++] = new PuppetVertex(
                new Vector2(positions[v].X, positions[v].Y), color, screen, region.Map(new Vector2(uv.X, uv.Y)), clip);
        }

        // Whole triangles only; a triangle with an index past the mesh (the loader reports those) would read another part's vertices.
        for (var i = 0; i + 2 < indices.Length; i += 3)
        {
            int a = indices[i], b = indices[i + 1], c = indices[i + 2];
            if (a >= positions.Length || b >= positions.Length || c >= positions.Length)
                continue;
            _indices[_indexCount++] = baseVertex + a;
            _indices[_indexCount++] = baseVertex + b;
            _indices[_indexCount++] = baseVertex + c;
        }
    }

    /// <summary>Draws the batch of Parts with fixed-function blending, if any.</summary>
    private void Flush()
    {
        if (_batchTexture is not null && _batchBlend is not null)
            DrawBatch(_batchTexture, _sampler, _puppetToClip, _partTechnique, _batchBlend, _batchMasked ? _maskTest : DepthStencilState.None);
        _vertexCount = 0;
        _indexCount = 0;
        _batchTexture = null;
        _batchBlend = null;
        _batchMasked = false;
    }

    private void DrawBatch(Texture2D texture, SamplerState sampler, Matrix transform, EffectTechnique technique,
        BlendState blend, DepthStencilState depthStencil)
    {
        if (_indexCount >= 3)
        {
            _device.BlendState = blend;
            _device.DepthStencilState = depthStencil;
            _device.SamplerStates[0] = sampler;
            _transform.SetValue(transform);
            _albedo.SetValue(texture);
            _effect.CurrentTechnique = technique;
            _effect.CurrentTechnique.Passes[0].Apply();
#if MONOGAME
            if (_streamBatches)
                DrawStreamed();
            else
                _device.DrawUserIndexedPrimitives(PrimitiveType.TriangleList, _vertices, 0, _vertexCount, _indices, 0, _indexCount / 3, PuppetVertex.Declaration);
#else
            // The declaration is passed explicitly: FNA would otherwise build it by reflection, which NativeAOT cannot do.
            _device.DrawUserIndexedPrimitives(PrimitiveType.TriangleList, _vertices, 0, _vertexCount, _indices, 0, _indexCount / 3, PuppetVertex.Declaration);
#endif
            LastDrawCalls++;
        }

        _vertexCount = 0;
        _indexCount = 0;
    }

#if MONOGAME
    /// <summary>
    /// Draws the batch through the dynamic buffers: appended after what earlier batches wrote (no overwrite, so the GPU
    /// can still read those), and from the start again, discarding the old contents, once a buffer is full.
    /// </summary>
    private void DrawStreamed()
    {
        var stride = PuppetVertex.Declaration.VertexStride;
        var vertexOptions = SetDataOptions.NoOverwrite;
        if (_streamVertexBuffer is null || _streamVertexBuffer.VertexCount < _vertexCount)
        {
            _streamVertexBuffer?.Dispose();
            _streamVertexBuffer = new DynamicVertexBuffer(_device, PuppetVertex.Declaration, Math.Max(StreamVertices, _vertexCount * 2), BufferUsage.WriteOnly);
            (_streamVertexCursor, vertexOptions) = (0, SetDataOptions.Discard);
        }
        else if (_streamVertexCursor + _vertexCount > _streamVertexBuffer.VertexCount)
        {
            (_streamVertexCursor, vertexOptions) = (0, SetDataOptions.Discard);
        }

        var indexOptions = SetDataOptions.NoOverwrite;
        if (_streamIndexBuffer is null || _streamIndexBuffer.IndexCount < _indexCount)
        {
            _streamIndexBuffer?.Dispose();
            _streamIndexBuffer = new DynamicIndexBuffer(_device, IndexElementSize.ThirtyTwoBits, Math.Max(StreamIndices, _indexCount * 2), BufferUsage.WriteOnly);
            (_streamIndexCursor, indexOptions) = (0, SetDataOptions.Discard);
        }
        else if (_streamIndexCursor + _indexCount > _streamIndexBuffer.IndexCount)
        {
            (_streamIndexCursor, indexOptions) = (0, SetDataOptions.Discard);
        }

        _streamVertexBuffer.SetData(_streamVertexCursor * stride, _vertices, 0, _vertexCount, stride, vertexOptions);
        _streamIndexBuffer.SetData(_streamIndexCursor * sizeof(int), _indices, 0, _indexCount, indexOptions);
        _device.SetVertexBuffer(_streamVertexBuffer);
        _device.Indices = _streamIndexBuffer;
        _device.DrawIndexedPrimitives(PrimitiveType.TriangleList, _streamVertexCursor, _streamIndexCursor, _indexCount / 3);
        _streamVertexCursor += _vertexCount;
        _streamIndexCursor += _indexCount;
    }

#endif
    /// <summary>The viewport pixels a puppet-space rectangle covers, grown by a pixel and clipped to the viewport.</summary>
    private Rectangle PixelRect(LayerBounds bounds)
    {
        if (bounds.IsEmpty)
            return Rectangle.Empty;
        var min = new Vector2(float.PositiveInfinity);
        var max = new Vector2(float.NegativeInfinity);
        Span<Vector2> corners = [new(bounds.Min.X, bounds.Min.Y), new(bounds.Max.X, bounds.Min.Y), new(bounds.Max.X, bounds.Max.Y), new(bounds.Min.X, bounds.Max.Y)];
        foreach (var corner in corners)
        {
            var p = Vector2.Transform(corner, _puppetToPixels);
            min = Vector2.Min(min, p);
            max = Vector2.Max(max, p);
        }

        var x0 = (int)Math.Clamp(MathF.Floor(min.X) - 1, 0, _width);
        var y0 = (int)Math.Clamp(MathF.Floor(min.Y) - 1, 0, _height);
        var x1 = (int)Math.Clamp(MathF.Ceiling(max.X) + 1, 0, _width);
        var y1 = (int)Math.Clamp(MathF.Ceiling(max.Y) + 1, 0, _height);
        return new Rectangle(x0, y0, x1 - x0, y1 - y0);
    }

#if MONOGAME
    /// <summary>MonoGame loads only effects compiled for its backend: one is embedded per supported backend.</summary>
    private static string EffectResource => PlatformInfo.GraphicsBackend switch
    {
        GraphicsBackend.OpenGL => "PaperDoll.MonoGame.Shaders.PaperDoll.ogl.mgfxo",
        GraphicsBackend.DirectX => "PaperDoll.MonoGame.Shaders.PaperDoll.dx11.mgfxo",
        var other => throw new NotSupportedException(
            $"PaperDoll.MonoGame has no effect for MonoGame's {other} backend; it supports DesktopGL (OpenGL) and WindowsDX (DirectX 11)."),
    };

#endif
    private static byte[] LoadEffectCode()
    {
        using var stream = typeof(PuppetRenderer).Assembly.GetManifestResourceStream(EffectResource)
                           ?? throw new InvalidOperationException($"The embedded effect '{EffectResource}' is missing.");
        var code = new byte[stream.Length];
        stream.ReadExactly(code);
        return code;
    }

    private static DepthStencilState Cached(DepthStencilState?[] cache, int value, CompareFunction function, StencilOperation pass) =>
        cache[value] ??= StencilState(function, pass, value);

    private static DepthStencilState StencilState(CompareFunction function, StencilOperation pass, int value) => new()
    {
        Name = $"PaperDoll.Stencil{function}{pass}{value}",
        ReferenceStencil = value,
        DepthBufferEnable = false,
        DepthBufferWriteEnable = false,
        StencilEnable = true,
        StencilFunction = function,
        StencilMask = 0xFF,
        StencilWriteMask = pass == StencilOperation.Keep ? 0 : 0xFF,
        StencilPass = pass,
        StencilFail = StencilOperation.Keep,
        StencilDepthBufferFail = StencilOperation.Keep,
    };

    /// <summary>Releases the effect, the offscreen layers and any other GPU resources the renderer made.</summary>
    public void Dispose()
    {
        _effect.Dispose();
        _white.Dispose();
#if MONOGAME
        _streamVertexBuffer?.Dispose();
        _streamIndexBuffer?.Dispose();
#endif
        _backdrop?.Dispose();
        foreach (var target in _targets)
            target?.Target.Dispose();
        _targets.Clear();
    }

    /// <summary>The render target kept for a nesting depth, and whether it has a stencil buffer.</summary>
    private sealed record LayerTarget(RenderTarget2D Target, bool Stencil);

    /// <summary>A layer being drawn: its render target and the viewport pixels it covers (its target's top-left pixel is the rectangle's).</summary>
    private readonly record struct Layer(RenderTarget2D Target, Rectangle Rect);

    /// <summary>What a layer holds: the puppet-space rectangle around what is drawn in it, and whether any of it is masked.</summary>
    private readonly record struct LayerPlan(LayerBounds Bounds, bool Stencil)
    {
        public static LayerPlan Empty => new(LayerBounds.Empty, false);
    }

    /// <summary>A puppet-space rectangle around what is drawn into a layer.</summary>
    private readonly record struct LayerBounds(System.Numerics.Vector2 Min, System.Numerics.Vector2 Max)
    {
        public static LayerBounds Empty => new(new System.Numerics.Vector2(float.PositiveInfinity), new System.Numerics.Vector2(float.NegativeInfinity));

        public bool IsEmpty => !(Min.X <= Max.X && Min.Y <= Max.Y);

        public LayerBounds Include(ReadOnlySpan<System.Numerics.Vector2> positions)
        {
            var min = Min;
            var max = Max;
            foreach (var p in positions)
            {
                min = System.Numerics.Vector2.Min(min, p);
                max = System.Numerics.Vector2.Max(max, p);
            }

            return new LayerBounds(min, max);
        }

        public LayerBounds Include(LayerBounds other) => other.IsEmpty ? this
            : new LayerBounds(System.Numerics.Vector2.Min(Min, other.Min), System.Numerics.Vector2.Max(Max, other.Max));
    }
}
