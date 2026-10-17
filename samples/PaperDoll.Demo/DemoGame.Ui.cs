using System.Numerics;
using ImGuiNET;
using PaperDoll.Snapshots;

namespace PaperDoll.Demo;

/// <summary>The ImGui debug interface: puppets on screen, the selected puppet's placement, parameters and snapshots, and its textures.</summary>
internal sealed partial class DemoGame
{
    private static readonly Vector4 ErrorColour = new(1f, 0.45f, 0.4f, 1f);
    private static readonly Vector4 OkColour = new(0.5f, 0.9f, 0.5f, 1f);
    private static readonly Vector4 DimColour = new(0.65f, 0.65f, 0.7f, 1f);

    private int _sampleIndex;
    private string _parameterFilter = "";
    private string _snapshotName = "rest";
    private string? _snapshotStatus;
    private bool _snapshotStatusIsError;
    private IReadOnlyList<SnapshotResult>? _checkResults;
    private string? _checkError;
    private int _textureSlot;
    private bool _showGpuTextures;
    private int _gpuTextureIndex;
    private (LoadedPuppet Puppet, int Slot)? _previewKey;
    private Microsoft.Xna.Framework.Graphics.Texture2D? _preview;
    private string? _previewError;

    private void DrawUi()
    {
        DrawPuppetsWindow();
        DrawSelectedWindow();
        DrawTexturesWindow();
        DrawCheckResultsWindow();
    }

    private void DrawPuppetsWindow()
    {
        ImGui.SetNextWindowPos(new Vector2(10, 10), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSize(new Vector2(340, 300), ImGuiCond.FirstUseEver);
        if (!ImGui.Begin("Puppets"))
        {
            ImGui.End();
            return;
        }

        var full = _slots.Count >= MaxSlots;
        ImGui.BeginDisabled(full);
        if (_samplePuppets.Count > 0)
        {
            var items = _samplePuppets.ToArray();
            ImGui.SetNextItemWidth(-60);
            ImGui.Combo("##sample", ref _sampleIndex, items, items.Length);
            ImGui.SameLine();
            if (ImGui.Button("Add"))
                AddPuppet(Path.Combine(_samplesRoot!, _samplePuppets[_sampleIndex]));
        }
        else
        {
            ImGui.TextColored(DimColour, "No .samples/ folder found (or PAPERDOLL_SAMPLES).");
        }

#if MONOGAME
        ImGui.TextColored(DimColour, "Drop puppet files on the window to open them.");
#else
        if (ImGui.Button("Open file..."))
            ShowOpenDialog();
        ImGui.SameLine();
#endif
        if (ImGui.Button("Blend test scene"))
            AddPuppet(TestScene.Name);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("A generated scene with every blend mode, masks and Composite layers.");
        ImGui.EndDisabled();
        ImGui.SameLine();
        ImGui.TextColored(DimColour, $"{_slots.Count} of {MaxSlots} on screen");

        ImGui.Separator();
        foreach (var slot in _slots.ToList())
        {
            ImGui.PushID(slot.GetHashCode());
            if (ImGui.SmallButton("Remove"))
                RemoveSlot(slot);
            ImGui.SameLine();
            ImGui.BeginDisabled(full);
            if (ImGui.SmallButton("Duplicate"))
                AddPuppet(slot.Source.Path);
            ImGui.EndDisabled();
            ImGui.SameLine();
            if (ImGui.Selectable(slot.Label, slot == _selected))
                _selected = slot;
            ImGui.PopID();
        }

        if (_slots.Count == 0)
            ImGui.TextColored(DimColour, "Add a sample or open a puppet file.");

        if (_status is not null)
        {
            ImGui.Separator();
            ImGui.PushTextWrapPos(0);
            if (_statusIsError)
                ImGui.PushStyleColor(ImGuiCol.Text, ErrorColour);
            ImGui.TextUnformatted(_status);
            if (_statusIsError)
                ImGui.PopStyleColor();
            ImGui.PopTextWrapPos();
        }

        ImGui.Separator();
        DrawRenderingOptions();

        ImGui.Separator();
        ImGui.TextColored(DimColour, "Drag a puppet to move it; mouse wheel to scale.");
        ImGui.End();
    }

    private void DrawRenderingOptions()
    {
        ImGui.Checkbox("Advanced blending", ref _advancedBlending);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Multiply, Screen, Overlay, Darken, Lighten, ColorDodge, ColorBurn, HardLight, SoftLight, Difference and Exclusion\n" +
                             "with their real equations, as nijilive draws them on GPUs with advanced blending. Off: nijilive's\n" +
                             "fixed-function fallback, which draws Overlay, Darken, ColorBurn, HardLight, SoftLight and Difference as Normal.");

        var atlas = _library.UseAtlas;
        if (ImGui.Checkbox("Texture atlas", ref atlas))
        {
            _library.UseAtlas = atlas;
            DisposePreview();
            _imgui.ReleaseDisposedTextures();
        }

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Pack each puppet's textures into a few pages, so neighbouring parts share a draw call. The picture should not change.");
        ImGui.SameLine();
        ImGui.Checkbox("Show bounds", ref _showBounds);

        var scaleIndex = Array.IndexOf(TextureScales, _library.TextureScale);
        ImGui.SetNextItemWidth(120);
        if (ImGui.Combo("Texture scale", ref scaleIndex, TextureScaleNames, TextureScaleNames.Length))
        {
            _library.TextureScale = TextureScales[scaleIndex];
            DisposePreview();
            _imgui.ReleaseDisposedTextures();
        }

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Upload textures at a lower resolution: half size takes a quarter of the GPU memory, and looks the same\n" +
                             "wherever a puppet is drawn at half its size or less.");

        var gpuTextures = _library.Loaded.Sum(l => l.Textures.GpuTextures.Count);
        var gpuMegabytes = _library.Loaded.Sum(l => GpuTextureBytes(l.Textures)) / 1048576.0;
        ImGui.TextColored(DimColour, $"{_itemsDrawn} parts, {_layersDrawn} layers, {_drawCalls} draw calls, {_drawMilliseconds:F2} ms CPU; " +
                                     $"{gpuTextures} GPU textures ({gpuMegabytes:F0} MB)");
    }

    private static readonly float[] TextureScales = [1f, 0.5f, 0.25f, 0.125f];
    private static readonly string[] TextureScaleNames = ["Full", "Half", "Quarter", "Eighth"];

    private void DrawSelectedWindow()
    {
        ImGui.SetNextWindowPos(new Vector2(GraphicsDevice.Viewport.Width - 430, 10), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSize(new Vector2(420, 620), ImGuiCond.FirstUseEver);
        if (!ImGui.Begin("Selected puppet"))
        {
            ImGui.End();
            return;
        }

        if (_selected is not { } slot)
        {
            ImGui.TextColored(DimColour, "Nothing selected.");
            ImGui.End();
            return;
        }

        var puppet = slot.Source.Puppet;
        ImGui.TextUnformatted(slot.Label);
        ImGui.TextColored(DimColour, slot.Source.SamplesPath ?? slot.Source.Path);

        if (ImGui.CollapsingHeader("Placement", ImGuiTreeNodeFlags.DefaultOpen))
        {
            var position = slot.Position;
            if (ImGui.DragFloat2("Position", ref position, 1f))
                slot.Position = position;
            var scale = slot.Scale;
            if (ImGui.SliderFloat("Scale", ref scale, MinScale, MaxScale, "%.3f", ImGuiSliderFlags.Logarithmic))
                slot.Scale = scale;
            var bounds = slot.Instance.Bounds;
            ImGui.TextColored(DimColour, $"Bounds: {bounds.Size.X:F0} x {bounds.Size.Y:F0} puppet px, centre ({bounds.Center.X:F0}, {bounds.Center.Y:F0})");
            var list = slot.Instance.DrawList;
            ImGui.TextColored(DimColour, $"Draw list: {list.Count(i => i.Kind == DrawItemKind.Part)} parts ({list.Count(i => i.Masks.Count > 0)} masked, " +
                                         $"{list.Count(i => i.Kind == DrawItemKind.Part && i.BlendMode != BlendMode.Normal)} not Normal), {list.Count(i => i.Kind == DrawItemKind.EndComposite)} Composites");
            ImGui.TextColored(DimColour, $"Instance time: {slot.Instance.Time:F1} s");
        }

        if (ImGui.CollapsingHeader("Puppet"))
            DrawPuppetInfo(puppet);

        if (ImGui.CollapsingHeader("Snapshots"))
            DrawSnapshots(slot);

        if (ImGui.CollapsingHeader("Physics", ImGuiTreeNodeFlags.DefaultOpen))
            DrawPhysics(slot);

        if (ImGui.CollapsingHeader($"Animations ({puppet.Animations.Count})###animations", puppet.Animations.Count > 0 ? ImGuiTreeNodeFlags.DefaultOpen : ImGuiTreeNodeFlags.None))
            DrawAnimations(slot);

        if (ImGui.CollapsingHeader("Parameters", ImGuiTreeNodeFlags.DefaultOpen))
            DrawParameters(slot);

        ImGui.End();
    }

    private static void DrawPuppetInfo(Puppet puppet)
    {
        var meta = puppet.Meta;
        ImGui.TextUnformatted($"Name: {meta.Name ?? "-"}");
        ImGui.TextUnformatted($"Artist: {meta.Artist ?? "-"}   Rigger: {meta.Rigger ?? "-"}");
        ImGui.TextUnformatted($"Written by: {meta.Version ?? "-"}");
        ImGui.TextUnformatted($"Nodes: {puppet.Nodes.Count} ({puppet.Nodes.Count(n => n.Kind == NodeKind.Part)} parts)");
        ImGui.TextUnformatted($"Parameters: {puppet.Parameters.Count}   Textures: {puppet.Textures.Count}   Animations: {puppet.Animations.Count}");

        if (ImGui.TreeNode($"Load notes ({puppet.Issues.Count})###issues"))
        {
            ImGui.PushTextWrapPos(0);
            foreach (var issue in puppet.Issues)
                ImGui.TextUnformatted(issue.ToString());
            ImGui.PopTextWrapPos();
            ImGui.TreePop();
        }
    }

    private void DrawSnapshots(PuppetSlot slot)
    {
        ImGui.PushTextWrapPos(0);
        if (_store is null || slot.Source.SamplesPath is null)
        {
            ImGui.TextColored(DimColour, "Snapshots are saved beside the samples, so only puppets inside .samples/ can have them.");
            ImGui.PopTextWrapPos();
            return;
        }

        ImGui.TextColored(DimColour, "Save a snapshot once this pose looks right: it records the parameter values and Core's output (the draw list and bounds), and the tests compare against it from then on.");
        ImGui.PopTextWrapPos();

        ImGui.SetNextItemWidth(200);
        ImGui.InputText("Name", ref _snapshotName, 100);
        var valid = Snapshot.IsValidName(_snapshotName);
        ImGui.BeginDisabled(!valid);
        if (ImGui.Button("Save snapshot"))
            SaveSnapshot(slot);
        ImGui.EndDisabled();

        if (valid && File.Exists(_store.PathOf(slot.Source.SamplesPath, _snapshotName)))
        {
            ImGui.SameLine();
            ImGui.TextColored(DimColour, "(replaces the saved one)");
        }
        else if (!valid)
        {
            ImGui.SameLine();
            ImGui.TextColored(ErrorColour, "letters, digits, - _ . only");
        }

        if (ImGui.Button("Check all saved snapshots"))
            CheckSnapshots();

        if (_snapshotStatus is not null)
            ImGui.TextColored(_snapshotStatusIsError ? ErrorColour : OkColour, _snapshotStatus);
    }

    private void SaveSnapshot(PuppetSlot slot)
    {
        try
        {
            // Every instance is stepped by the fixed frame time, so replay takes the same steps (unsettled physics matches).
            var step = IsFixedTimeStep ? (float)TargetElapsedTime.TotalSeconds : 0;
            var snapshot = Snapshot.Capture(_snapshotName, slot.Source.SamplesPath!, slot.Instance, step);
            var path = _store!.Save(snapshot);
            _snapshotStatus = $"Saved {Path.GetRelativePath(_store.SamplesRoot, path)}";
            _snapshotStatusIsError = false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _snapshotStatus = $"Could not save: {ex.Message}";
            _snapshotStatusIsError = true;
        }
    }

    private void CheckSnapshots()
    {
        try
        {
            _checkResults = SnapshotCheck.Run(_store!, _store!.LoadAll());
            _checkError = null;
            var failed = _checkResults.Count(r => !r.Passed);
            _snapshotStatus = _checkResults.Count == 0 ? "No snapshots saved yet." : $"{_checkResults.Count - failed} of {_checkResults.Count} snapshots match.";
            _snapshotStatusIsError = failed > 0;
        }
        catch (Exception ex) when (ex is SnapshotFormatException or IOException or UnauthorizedAccessException)
        {
            _checkResults = null;
            _checkError = ex.Message;
            _snapshotStatus = "Could not read the snapshots.";
            _snapshotStatusIsError = true;
        }
    }

    private void DrawCheckResultsWindow()
    {
        if (_checkResults is null && _checkError is null)
            return;

        ImGui.SetNextWindowSize(new Vector2(560, 300), ImGuiCond.FirstUseEver);
        var open = true;
        if (ImGui.Begin("Snapshot check", ref open))
        {
            ImGui.PushTextWrapPos(0);
            if (_checkError is not null)
                ImGui.TextColored(ErrorColour, _checkError);
            foreach (var result in _checkResults ?? [])
            {
                var label = $"{result.Snapshot.PuppetPath} / {result.Snapshot.Name}";
                if (result.Passed)
                {
                    ImGui.TextColored(OkColour, $"match     {label}");
                    continue;
                }

                ImGui.TextColored(ErrorColour, $"DIFFERS   {label}");
                ImGui.Indent();
                if (result.Error is not null)
                    ImGui.TextUnformatted(result.Error);
                foreach (var difference in result.Differences.Take(50))
                    ImGui.TextUnformatted(difference);
                if (result.Differences.Count > 50)
                    ImGui.TextColored(DimColour, $"... and {result.Differences.Count - 50} more");
                ImGui.Unindent();
            }

            ImGui.PopTextWrapPos();
        }

        ImGui.End();
        if (!open)
        {
            _checkResults = null;
            _checkError = null;
        }
    }

    private void DrawPhysics(PuppetSlot slot)
    {
        var instance = slot.Instance;
        var puppet = instance.Puppet;
        var nodes = puppet.Nodes.OfType<SimplePhysics>().ToList();
        var driven = puppet.Parameters.Count(p => p.PhysicsDriver is not null);
        ImGui.TextColored(DimColour, $"{nodes.Count} physics nodes drive {driven} parameters" +
                                     (nodes.Count > driven ? $" ({nodes.Count - driven} name none in the puppet)." : "."));
        if (nodes.Count == 0)
            return;

        var enabled = instance.PhysicsEnabled;
        if (ImGui.Checkbox("Physics on", ref enabled))
            instance.PhysicsEnabled = enabled;
        ImGui.SameLine();
        if (ImGui.Button("Reset physics"))
            instance.ResetPhysics();
        ImGui.SameLine();
        if (ImGui.Button("All on"))
        {
            foreach (var other in _slots)
                other.Instance.PhysicsEnabled = true;
        }

        ImGui.SameLine();
        if (ImGui.Button("All off"))
        {
            foreach (var other in _slots)
                other.Instance.PhysicsEnabled = false;
        }

        ImGui.PushTextWrapPos(0);
        ImGui.TextColored(DimColour, "Drag the puppet to swing its physics. While physics is on, the parameters it drives follow the simulation.");
        ImGui.PopTextWrapPos();
    }

    private static void DrawAnimations(PuppetSlot slot)
    {
        var instance = slot.Instance;
        if (instance.Animations.Count == 0)
        {
            ImGui.TextColored(DimColour, "This puppet has no animations.");
            return;
        }

        ImGui.PushTextWrapPos(0);
        ImGui.TextColored(DimColour, "Play runs once and stops; Loop repeats from the end of the lead-in to the start of the lead-out. " +
                                     "Stop lets a loop finish and play its lead-out (if it has one); Stop now goes straight back. " +
                                     "Dragging Frame pauses the animation there. While an animation drives a parameter, its slider shows the value it is evaluated at after '->'.");
        ImGui.PopTextWrapPos();
        if (ImGui.Button("Stop all"))
            instance.StopAnimations();
        ImGui.SameLine();
        if (ImGui.Button("Stop all now"))
            instance.StopAnimations(immediate: true);

        foreach (var playback in instance.Animations)
        {
            var animation = playback.Animation;
            ImGui.PushID(playback.Name);
            ImGui.SeparatorText(playback.Name);
            var leads = (playback.HasLeadIn ? $", lead-in to {animation.LeadIn}" : "") + (playback.HasLeadOut ? $", lead-out from {animation.LeadOut}" : "");
            ImGui.TextColored(DimColour, $"{animation.Length} frames at {1 / animation.Timestep:F0} fps ({animation.Length * animation.Timestep:F2} s){leads}, " +
                                         $"{animation.Lanes.Count} lane(s){(animation.Additive ? "; marked additive (no effect, as in nijilive)" : "")}");

            if (ImGui.Button("Play"))
                playback.Play();
            ImGui.SameLine();
            if (ImGui.Button("Loop"))
                playback.Play(loop: true);
            ImGui.SameLine();
            ImGui.BeginDisabled(!playback.IsRunning);
            if (playback.IsPaused ? ImGui.Button("Resume") : ImGui.Button("Pause"))
            {
                if (playback.IsPaused)
                    playback.Play();
                else
                    playback.Pause();
            }

            ImGui.SameLine();
            if (ImGui.Button("Stop"))
                playback.Stop();
            ImGui.SameLine();
            if (ImGui.Button("Stop now"))
                playback.Stop(immediate: true);
            ImGui.EndDisabled();
            ImGui.SameLine();
            ImGui.TextColored(playback.IsRunning ? OkColour : DimColour, Status(playback));

            var frame = playback.FramePosition;
            ImGui.SetNextItemWidth(-60);
            if (ImGui.SliderFloat("Frame", ref frame, 0, Math.Max(animation.Length - 1, 0), "%.1f"))
            {
                if (!playback.IsRunning)
                    playback.Play(loop: true);
                playback.Pause();
                playback.Seek(frame);
            }

            var strength = playback.Strength;
            ImGui.SetNextItemWidth(140);
            if (ImGui.SliderFloat("Strength", ref strength, 0, 1, "%.2f"))
                playback.Strength = strength;
            ImGui.SameLine();
            var speed = playback.Speed;
            ImGui.SetNextItemWidth(140);
            if (ImGui.SliderFloat("Speed", ref speed, 0, 4, "%.2f"))
                playback.Speed = speed;
            ImGui.PopID();
        }
    }

    private static string Status(AnimationPlayback playback)
    {
        if (!playback.IsRunning)
            return "stopped";
        var state = playback.IsPaused ? "paused" :
            playback.IsStopping ? "playing out" :
            playback.IsLooping ? $"looping ({playback.LoopCount})" : "playing";
        return $"{state}, frame {playback.Frame}";
    }

    private static string SliderFormat(float host, float evaluated) =>
        MathF.Abs(host - evaluated) > 1e-4f ? $"%.3f -> {evaluated:0.000}" : "%.3f";

    private void DrawParameters(PuppetSlot slot)
    {
        var instance = slot.Instance;
        var parameters = instance.Puppet.Parameters;

        ImGui.PushTextWrapPos(0);
        ImGui.TextColored(DimColour, "Right-click a slider to reset it. Greyed-out sliders are driven by physics.");
        ImGui.PopTextWrapPos();
        ImGui.SetNextItemWidth(-90);
        ImGui.InputTextWithHint("##filter", "filter", ref _parameterFilter, 100);
        ImGui.SameLine();
        if (ImGui.Button("Reset all"))
            instance.ResetParameters();

        if (!ImGui.BeginChild("params", Vector2.Zero, ImGuiChildFlags.Borders))
        {
            ImGui.EndChild();
            return;
        }

        foreach (var parameter in parameters)
        {
            if (_parameterFilter.Length > 0 && !parameter.Name.Contains(_parameterFilter, StringComparison.OrdinalIgnoreCase))
                continue;

            ImGui.PushID((int)parameter.Uuid);
            var drivenByPhysics = instance.PhysicsEnabled && parameter.PhysicsDriver is not null;
            ImGui.BeginDisabled(drivenByPhysics);
            var value = instance.GetParameterValue(parameter);
            var evaluated = instance.GetEvaluatedValue(parameter);
            var changed = false;
            ImGui.SetNextItemWidth(-160);
            if (parameter.IsVec2)
            {
                changed |= ImGui.SliderFloat($"{parameter.Name} X", ref value.X, parameter.Min.X, parameter.Max.X, SliderFormat(value.X, evaluated.X));
                var resetX = ImGui.IsItemClicked(ImGuiMouseButton.Right);
                ImGui.SetNextItemWidth(-160);
                changed |= ImGui.SliderFloat($"{parameter.Name} Y", ref value.Y, parameter.Min.Y, parameter.Max.Y, SliderFormat(value.Y, evaluated.Y));
                var resetY = ImGui.IsItemClicked(ImGuiMouseButton.Right);
                if (resetX)
                    (value.X, changed) = (parameter.Defaults.X, true);
                if (resetY)
                    (value.Y, changed) = (parameter.Defaults.Y, true);
            }
            else
            {
                changed |= ImGui.SliderFloat(parameter.Name, ref value.X, parameter.Min.X, parameter.Max.X, SliderFormat(value.X, evaluated.X));
                if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                    (value.X, changed) = (parameter.Defaults.X, true);
            }

            if (changed)
                instance.SetParameterValue(parameter, value);
            ImGui.EndDisabled();
            ImGui.PopID();
        }

        ImGui.EndChild();
    }

    private void DrawTexturesWindow()
    {
        ImGui.SetNextWindowPos(new Vector2(10, 320), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSize(new Vector2(480, 360), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowCollapsed(true, ImGuiCond.FirstUseEver);
        if (!ImGui.Begin("Textures"))
        {
            ImGui.End();
            return;
        }

        if (_selected is not { } slot)
        {
            ImGui.TextColored(DimColour, "Nothing selected.");
            ImGui.End();
            return;
        }

        var textures = slot.Source.Textures;
        var puppet = slot.Source.Puppet;
        if (textures.Count == 0)
        {
            ImGui.TextColored(DimColour, "This puppet has no textures.");
            ImGui.End();
            return;
        }

        ImGui.Checkbox("Show GPU textures (atlas pages)", ref _showGpuTextures);
        _textureSlot = Math.Clamp(_textureSlot, 0, textures.Count - 1);
        textures.LoadAll();
        var gpu = textures.GpuTextures;
        _gpuTextureIndex = Math.Clamp(_gpuTextureIndex, 0, Math.Max(0, gpu.Count - 1));
        if (ImGui.BeginChild("slots", new Vector2(170, 0), ImGuiChildFlags.Borders | ImGuiChildFlags.ResizeX))
        {
            if (_showGpuTextures)
            {
                for (var i = 0; i < gpu.Count; i++)
                {
                    if (ImGui.Selectable($"{i}: {gpu[i].Width}x{gpu[i].Height}", i == _gpuTextureIndex))
                        _gpuTextureIndex = i;
                }
            }
            else
            {
                for (var i = 0; i < textures.Count; i++)
                {
                    var source = puppet.Textures[i];
                    var label = source.IsEmpty ? $"{i}: empty" : $"{i}: {source.Width}x{source.Height} {source.Encoding?.ToString() ?? "?"}";
                    if (ImGui.Selectable(label, i == _textureSlot))
                        _textureSlot = i;
                }
            }
        }

        ImGui.EndChild();
        ImGui.SameLine();

        if (ImGui.BeginChild("preview", Vector2.Zero, ImGuiChildFlags.Borders, ImGuiWindowFlags.HorizontalScrollbar))
        {
            if (_showGpuTextures)
            {
                if (gpu.Count == 0)
                    ImGui.TextColored(DimColour, "No GPU textures.");
                else
                    DrawPreview(gpu[_gpuTextureIndex], $"{(textures.UsesAtlas ? "Atlas page" : "Texture")} {_gpuTextureIndex}, {gpu[_gpuTextureIndex].Width}x{gpu[_gpuTextureIndex].Height}");
            }
            else
            {
                var texture = PreviewOf(slot.Source, _textureSlot);
                if (texture is null)
                    ImGui.TextColored(_previewError is null ? DimColour : ErrorColour, _previewError ?? "Cannot be shown (empty or not decodable).");
                else
                    DrawPreview(texture, $"Slot {_textureSlot}, {texture.Width}x{texture.Height}, premultiplied alpha");
            }
        }

        ImGui.EndChild();
        ImGui.End();
    }

    private void DrawPreview(Microsoft.Xna.Framework.Graphics.Texture2D texture, string caption)
    {
        ImGui.TextColored(DimColour, caption);
        var available = ImGui.GetContentRegionAvail();
        var fit = Math.Min(1f, Math.Min(available.X / texture.Width, available.Y / texture.Height));
        var size = new Vector2(texture.Width, texture.Height) * Math.Max(fit, 0.01f);
        var origin = ImGui.GetCursorScreenPos();
        DrawChecker(origin, size);
        ImGui.Image(_imgui.GetTextureId(texture), size);
    }

    /// <summary>
    /// The slot as its own texture, decoded for the browser only (in atlas mode the slot is cropped into a page). One
    /// preview is kept at a time.
    /// </summary>
    private Microsoft.Xna.Framework.Graphics.Texture2D? PreviewOf(LoadedPuppet loaded, int slot)
    {
        if (_previewKey == (loaded, slot))
            return _preview;

        DisposePreview();
        _previewKey = (loaded, slot);
        var source = loaded.Puppet.Textures[slot];
        if (!source.CanDecode)
            return null;
        try
        {
            var image = source.Decode();
            _preview = new Microsoft.Xna.Framework.Graphics.Texture2D(GraphicsDevice, image.Width, image.Height);
            _preview.SetData(image.Rgba);
        }
        catch (PuppetFormatException ex)
        {
            _previewError = ex.Message;
        }

        return _preview;
    }

    private void DisposePreview()
    {
        _preview?.Dispose();
        _preview = null;
        _previewKey = null;
        _previewError = null;
        _imgui.ReleaseDisposedTextures();
    }

    /// <summary>A checkerboard behind a texture preview, so transparent areas show.</summary>
    private static void DrawChecker(Vector2 origin, Vector2 size)
    {
        const float cell = 12f;
        var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(origin, origin + size, 0xFF505050u);
        for (var y = 0f; y < size.Y; y += cell)
        {
            for (var x = ((int)(y / cell) % 2) * cell; x < size.X; x += cell * 2)
                draw.AddRectFilled(origin + new Vector2(x, y), origin + new Vector2(Math.Min(x + cell, size.X), Math.Min(y + cell, size.Y)), 0xFF707070u);
        }
    }
}
