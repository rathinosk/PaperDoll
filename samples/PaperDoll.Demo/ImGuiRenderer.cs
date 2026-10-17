// Dear ImGui backend for FNA and MonoGame.
// Adapted from ImGui.NET 1.91.6.1's XNA sample (src/ImGui.NET.SampleProgram.XNA/ImGuiRenderer.cs, DrawVertDeclaration.cs),
// MIT, Copyright (c) 2017 Eric Mellino and ImGui.NET contributors. See lib/ImGui.NET/LICENSE.
// Changed: FNA text input (TextInputEXT; MonoGame's Window.TextInput), no horizontal wheel on FNA (it has none), right-hand
// modifier keys, texture IDs per Texture2D, nullable annotations.

using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using ImGuiNET;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Vector2 = System.Numerics.Vector2;

namespace PaperDoll.Demo;

/// <summary>Feeds FNA's (or MonoGame's) input to Dear ImGui and draws its output.</summary>
internal sealed class ImGuiRenderer : IDisposable
{
    private const float WheelDelta = 120f;

    private static readonly Keys[] AllKeys = Enum.GetValues<Keys>();

    private static readonly VertexDeclaration VertexDeclaration = new(
        Unsafe.SizeOf<ImDrawVert>(),
        new VertexElement(0, VertexElementFormat.Vector2, VertexElementUsage.Position, 0),
        new VertexElement(8, VertexElementFormat.Vector2, VertexElementUsage.TextureCoordinate, 0),
        new VertexElement(16, VertexElementFormat.Color, VertexElementUsage.Color, 0));

    private readonly Game _game;
    private readonly GraphicsDevice _device;
    private readonly BasicEffect _effect;
    private readonly RasterizerState _rasterizer = new() { CullMode = CullMode.None, ScissorTestEnable = true };
    private readonly Dictionary<IntPtr, Texture2D> _textures = [];
    private readonly Dictionary<Texture2D, IntPtr> _textureIds = [];

    private byte[] _vertexData = [];
    private VertexBuffer? _vertexBuffer;
    private int _vertexBufferSize;
    private byte[] _indexData = [];
    private IndexBuffer? _indexBuffer;
    private int _indexBufferSize;

    private int _nextTextureId = 1;
    private IntPtr _fontTextureId;
    private int _scrollWheel;
#if MONOGAME
    private int _horizontalScrollWheel;
#else
    private bool _textInputActive;
#endif

    /// <param name="game">The game whose window and device ImGui uses.</param>
    /// <param name="iniPath">Where ImGui keeps window layout between runs, or <see langword="null"/> to keep none.</param>
    public unsafe ImGuiRenderer(Game game, string? iniPath)
    {
        _game = game;
        _device = game.GraphicsDevice;
        _effect = new BasicEffect(_device) { TextureEnabled = true, VertexColorEnabled = true };

        ImGui.SetCurrentContext(ImGui.CreateContext());
        // ImGui's default is imgui.ini in the working directory; the string must outlive the context, so it is never freed.
        ImGui.GetIO().NativePtr->IniFilename = iniPath is null ? null : (byte*)Marshal.StringToCoTaskMemUTF8(iniPath);
#if MONOGAME
        game.Window.TextInput += OnTextInput;
#else
        TextInputEXT.TextInput += OnTextInput;
#endif
        RebuildFontAtlas();
    }

    /// <summary>The ImGui texture ID for a texture, bound on first use.</summary>
    public IntPtr GetTextureId(Texture2D texture)
    {
        if (_textureIds.TryGetValue(texture, out var id))
            return id;

        id = new IntPtr(_nextTextureId++);
        _textures.Add(id, texture);
        _textureIds.Add(texture, id);
        return id;
    }

    /// <summary>Forgets textures that have been disposed since they were bound.</summary>
    public void ReleaseDisposedTextures()
    {
        foreach (var (texture, id) in _textureIds.Where(p => p.Key.IsDisposed).ToList())
        {
            _textureIds.Remove(texture);
            _textures.Remove(id);
        }
    }

    /// <summary>Starts an ImGui frame. Call before any ImGui widgets.</summary>
    public void BeforeLayout(GameTime gameTime)
    {
        var io = ImGui.GetIO();
        io.DeltaTime = Math.Max((float)gameTime.ElapsedGameTime.TotalSeconds, 1e-5f);
        UpdateInput(io);
        ImGui.NewFrame();
    }

    /// <summary>Ends the ImGui frame and draws it.</summary>
    public void AfterLayout()
    {
        ImGui.Render();
        RenderDrawData(ImGui.GetDrawData());

#if !MONOGAME
        // Text input events only arrive while SDL's text input is on, so follow what ImGui wants.
        var wantText = ImGui.GetIO().WantTextInput;
        if (wantText != _textInputActive)
        {
            _textInputActive = wantText;
            if (wantText)
                TextInputEXT.StartTextInput();
            else
                TextInputEXT.StopTextInput();
        }
#endif
    }

    public void Dispose()
    {
#if MONOGAME
        _game.Window.TextInput -= OnTextInput;
#else
        TextInputEXT.TextInput -= OnTextInput;
#endif
        _vertexBuffer?.Dispose();
        _indexBuffer?.Dispose();
        _effect.Dispose();
        _rasterizer.Dispose();
        if (_textures.Remove(_fontTextureId, out var font))
            font.Dispose();
        ImGui.DestroyContext();
    }

    private unsafe void RebuildFontAtlas()
    {
        var io = ImGui.GetIO();
        io.Fonts.GetTexDataAsRGBA32(out byte* pixelData, out var width, out var height, out var bytesPerPixel);
        var pixels = new byte[width * height * bytesPerPixel];
        Marshal.Copy(new IntPtr(pixelData), pixels, 0, pixels.Length);

        var texture = new Texture2D(_device, width, height, false, SurfaceFormat.Color);
        texture.SetData(pixels);
        _fontTextureId = GetTextureId(texture);
        io.Fonts.SetTexID(_fontTextureId);
        io.Fonts.ClearTexData();
    }

#if MONOGAME
    private static void OnTextInput(object? sender, TextInputEventArgs e)
    {
        if (!char.IsControl(e.Character))
            ImGui.GetIO().AddInputCharacter(e.Character);
    }
#else
    private static void OnTextInput(char c)
    {
        if (c != '\t')
            ImGui.GetIO().AddInputCharacter(c);
    }
#endif

    private void UpdateInput(ImGuiIOPtr io)
    {
        io.DisplaySize = new Vector2(_device.PresentationParameters.BackBufferWidth, _device.PresentationParameters.BackBufferHeight);
        io.DisplayFramebufferScale = Vector2.One;
        if (!_game.IsActive)
            return;

        var mouse = Mouse.GetState();
        io.AddMousePosEvent(mouse.X, mouse.Y);
        io.AddMouseButtonEvent(0, mouse.LeftButton == ButtonState.Pressed);
        io.AddMouseButtonEvent(1, mouse.RightButton == ButtonState.Pressed);
        io.AddMouseButtonEvent(2, mouse.MiddleButton == ButtonState.Pressed);
        io.AddMouseButtonEvent(3, mouse.XButton1 == ButtonState.Pressed);
        io.AddMouseButtonEvent(4, mouse.XButton2 == ButtonState.Pressed);
#if MONOGAME
        io.AddMouseWheelEvent((mouse.HorizontalScrollWheelValue - _horizontalScrollWheel) / WheelDelta, (mouse.ScrollWheelValue - _scrollWheel) / WheelDelta);
        _horizontalScrollWheel = mouse.HorizontalScrollWheelValue;
#else
        io.AddMouseWheelEvent(0f, (mouse.ScrollWheelValue - _scrollWheel) / WheelDelta); // FNA has no horizontal wheel
#endif
        _scrollWheel = mouse.ScrollWheelValue;

        var keyboard = Keyboard.GetState();
        foreach (var key in AllKeys)
        {
            if (TryMapKey(key, out var imguiKey))
                io.AddKeyEvent(imguiKey, keyboard.IsKeyDown(key));
        }

        io.AddKeyEvent(ImGuiKey.ModShift, keyboard.IsKeyDown(Keys.LeftShift) || keyboard.IsKeyDown(Keys.RightShift));
        io.AddKeyEvent(ImGuiKey.ModCtrl, keyboard.IsKeyDown(Keys.LeftControl) || keyboard.IsKeyDown(Keys.RightControl));
        io.AddKeyEvent(ImGuiKey.ModAlt, keyboard.IsKeyDown(Keys.LeftAlt) || keyboard.IsKeyDown(Keys.RightAlt));
    }

    private static bool TryMapKey(Keys key, out ImGuiKey imguiKey)
    {
        imguiKey = key switch
        {
            Keys.Back => ImGuiKey.Backspace,
            Keys.Tab => ImGuiKey.Tab,
            Keys.Enter => ImGuiKey.Enter,
            Keys.Escape => ImGuiKey.Escape,
            Keys.Space => ImGuiKey.Space,
            Keys.PageUp => ImGuiKey.PageUp,
            Keys.PageDown => ImGuiKey.PageDown,
            Keys.End => ImGuiKey.End,
            Keys.Home => ImGuiKey.Home,
            Keys.Left => ImGuiKey.LeftArrow,
            Keys.Right => ImGuiKey.RightArrow,
            Keys.Up => ImGuiKey.UpArrow,
            Keys.Down => ImGuiKey.DownArrow,
            Keys.Insert => ImGuiKey.Insert,
            Keys.Delete => ImGuiKey.Delete,
            >= Keys.D0 and <= Keys.D9 => ImGuiKey._0 + (key - Keys.D0),
            >= Keys.A and <= Keys.Z => ImGuiKey.A + (key - Keys.A),
            >= Keys.NumPad0 and <= Keys.NumPad9 => ImGuiKey.Keypad0 + (key - Keys.NumPad0),
            Keys.Multiply => ImGuiKey.KeypadMultiply,
            Keys.Add => ImGuiKey.KeypadAdd,
            Keys.Subtract => ImGuiKey.KeypadSubtract,
            Keys.Decimal => ImGuiKey.KeypadDecimal,
            Keys.Divide => ImGuiKey.KeypadDivide,
            >= Keys.F1 and <= Keys.F12 => ImGuiKey.F1 + (key - Keys.F1),
            Keys.LeftShift => ImGuiKey.LeftShift,
            Keys.RightShift => ImGuiKey.RightShift,
            Keys.LeftControl => ImGuiKey.LeftCtrl,
            Keys.RightControl => ImGuiKey.RightCtrl,
            Keys.LeftAlt => ImGuiKey.LeftAlt,
            Keys.RightAlt => ImGuiKey.RightAlt,
            Keys.OemSemicolon => ImGuiKey.Semicolon,
            Keys.OemPlus => ImGuiKey.Equal,
            Keys.OemComma => ImGuiKey.Comma,
            Keys.OemMinus => ImGuiKey.Minus,
            Keys.OemPeriod => ImGuiKey.Period,
            Keys.OemQuestion => ImGuiKey.Slash,
            Keys.OemTilde => ImGuiKey.GraveAccent,
            Keys.OemOpenBrackets => ImGuiKey.LeftBracket,
            Keys.OemCloseBrackets => ImGuiKey.RightBracket,
            Keys.OemPipe => ImGuiKey.Backslash,
            Keys.OemQuotes => ImGuiKey.Apostrophe,
            _ => ImGuiKey.None,
        };
        return imguiKey != ImGuiKey.None;
    }

    private void RenderDrawData(ImDrawDataPtr drawData)
    {
        if (drawData.TotalVtxCount == 0)
            return;

        var lastViewport = _device.Viewport;
        var lastScissor = _device.ScissorRectangle;
        var lastRasterizer = _device.RasterizerState;
        var lastDepthStencil = _device.DepthStencilState;
        var lastBlendFactor = _device.BlendFactor;
        var lastBlend = _device.BlendState;

        _device.BlendFactor = Color.White;
        _device.BlendState = BlendState.NonPremultiplied;
        _device.RasterizerState = _rasterizer;
        _device.DepthStencilState = DepthStencilState.None;
        _device.Viewport = new Viewport(0, 0, _device.PresentationParameters.BackBufferWidth, _device.PresentationParameters.BackBufferHeight);

        UpdateBuffers(drawData);
        RenderCommandLists(drawData);

        _device.Viewport = lastViewport;
        _device.ScissorRectangle = lastScissor;
        _device.RasterizerState = lastRasterizer;
        _device.DepthStencilState = lastDepthStencil;
        _device.BlendState = lastBlend;
        _device.BlendFactor = lastBlendFactor;
    }

    private unsafe void UpdateBuffers(ImDrawDataPtr drawData)
    {
        var vertexSize = VertexDeclaration.VertexStride;
        if (drawData.TotalVtxCount > _vertexBufferSize)
        {
            _vertexBuffer?.Dispose();
            _vertexBufferSize = (int)(drawData.TotalVtxCount * 1.5f);
            _vertexBuffer = new VertexBuffer(_device, VertexDeclaration, _vertexBufferSize, BufferUsage.None);
            _vertexData = new byte[_vertexBufferSize * vertexSize];
        }

        if (drawData.TotalIdxCount > _indexBufferSize)
        {
            _indexBuffer?.Dispose();
            _indexBufferSize = (int)(drawData.TotalIdxCount * 1.5f);
            _indexBuffer = new IndexBuffer(_device, IndexElementSize.SixteenBits, _indexBufferSize, BufferUsage.None);
            _indexData = new byte[_indexBufferSize * sizeof(ushort)];
        }

        var vertexOffset = 0;
        var indexOffset = 0;
        for (var n = 0; n < drawData.CmdListsCount; n++)
        {
            var list = drawData.CmdLists[n];
            fixed (byte* vertexDst = &_vertexData[vertexOffset * vertexSize])
            fixed (byte* indexDst = &_indexData[indexOffset * sizeof(ushort)])
            {
                Buffer.MemoryCopy((void*)list.VtxBuffer.Data, vertexDst, _vertexData.Length - vertexOffset * vertexSize, list.VtxBuffer.Size * vertexSize);
                Buffer.MemoryCopy((void*)list.IdxBuffer.Data, indexDst, _indexData.Length - indexOffset * sizeof(ushort), list.IdxBuffer.Size * sizeof(ushort));
            }

            vertexOffset += list.VtxBuffer.Size;
            indexOffset += list.IdxBuffer.Size;
        }

        _vertexBuffer!.SetData(_vertexData, 0, drawData.TotalVtxCount * vertexSize);
        _indexBuffer!.SetData(_indexData, 0, drawData.TotalIdxCount * sizeof(ushort));
    }

    private void RenderCommandLists(ImDrawDataPtr drawData)
    {
        _device.SetVertexBuffer(_vertexBuffer);
        _device.Indices = _indexBuffer;

        var io = ImGui.GetIO();
        _effect.World = Matrix.Identity;
        _effect.View = Matrix.Identity;
        _effect.Projection = Matrix.CreateOrthographicOffCenter(0f, io.DisplaySize.X, io.DisplaySize.Y, 0f, -1f, 1f);

        var vertexOffset = 0;
        var indexOffset = 0;
        for (var n = 0; n < drawData.CmdListsCount; n++)
        {
            var list = drawData.CmdLists[n];
            for (var i = 0; i < list.CmdBuffer.Size; i++)
            {
                var command = list.CmdBuffer[i];
                if (command.ElemCount == 0)
                    continue;
                if (!_textures.TryGetValue(command.TextureId, out var texture))
                    throw new InvalidOperationException($"ImGui used texture ID {command.TextureId}, which is not bound.");

                _device.ScissorRectangle = new Rectangle(
                    (int)command.ClipRect.X,
                    (int)command.ClipRect.Y,
                    (int)(command.ClipRect.Z - command.ClipRect.X),
                    (int)(command.ClipRect.W - command.ClipRect.Y));

                _effect.Texture = texture;
                foreach (var pass in _effect.CurrentTechnique.Passes)
                {
                    pass.Apply();
#if MONOGAME
                    // MonoGame marks the XNA overload with minVertexIndex and numVertices obsolete.
                    _device.DrawIndexedPrimitives(
                        PrimitiveType.TriangleList,
                        (int)command.VtxOffset + vertexOffset,
                        (int)command.IdxOffset + indexOffset,
                        (int)command.ElemCount / 3);
#else
                    _device.DrawIndexedPrimitives(
                        PrimitiveType.TriangleList,
                        (int)command.VtxOffset + vertexOffset,
                        0,
                        list.VtxBuffer.Size,
                        (int)command.IdxOffset + indexOffset,
                        (int)command.ElemCount / 3);
#endif
                }
            }

            vertexOffset += list.VtxBuffer.Size;
            indexOffset += list.IdxBuffer.Size;
        }
    }
}
