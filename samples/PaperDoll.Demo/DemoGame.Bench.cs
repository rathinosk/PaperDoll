using System.Diagnostics;
#if MONOGAME
using PaperDoll.MonoGame;
#else
using PaperDoll.Fna;
#endif

namespace PaperDoll.Demo;

/// <summary>
/// The demo's benchmark (<c>--bench</c>): every animation loops, every parameter physics does not drive sweeps through its
/// range as a host would drive it, physics stays on, and the CPU time and managed allocations of PaperDoll's update and
/// draw are measured over a number of frames, printed, and the demo exits.
/// </summary>
internal sealed partial class DemoGame
{
    private const int BenchWarmUpFrames = 120;

    private readonly Stopwatch _benchClock = new();
    private double[] _benchUpdate = [];
    private double[] _benchDraw = [];
    private long _benchSectionStart;
    private long _benchAllocatedUpdate;
    private long _benchAllocatedDraw;
    private int _benchFrame = -1;
    private int _benchGen0, _benchGen1, _benchGen2;
    private int _benchMaxDrawCalls, _benchMaxLayers;

    /// <summary>If above 0, the demo runs the benchmark for this many frames (after a warm-up) and exits.</summary>
    public int BenchFrames { get; init; }

    private bool Benchmarking => BenchFrames > 0;

    private bool BenchMeasuring => Benchmarking && _benchFrame >= BenchWarmUpFrames;

    private void StartBench()
    {
        _benchUpdate = new double[BenchFrames];
        _benchDraw = new double[BenchFrames];
        _benchFrame = 0;
        foreach (var slot in _slots)
        {
            foreach (var playback in slot.Instance.Animations)
                playback.Play(loop: true);
        }
    }

    /// <summary>Sweeps every parameter not driven by physics, each with its own period and phase.</summary>
    private static void SweepParameters(PuppetSlot slot)
    {
        var time = (float)slot.Instance.Time;
        var parameters = slot.Instance.Puppet.Parameters;
        for (var i = 0; i < parameters.Count; i++)
        {
            var parameter = parameters[i];
            if (parameter.PhysicsDriver is not null)
                continue;
            var period = 1.5f + parameter.Index % 7 * 0.4f;
            var phase = parameter.Index * 0.7f;
            var x = 0.5f + 0.5f * MathF.Sin(time * MathF.Tau / period + phase);
            var y = 0.5f + 0.5f * MathF.Cos(time * MathF.Tau / (period * 1.3f) + phase);
            var value = parameter.Min + (parameter.Max - parameter.Min) * new System.Numerics.Vector2(x, y);
            slot.Instance.SetParameterValue(parameter, value);
        }
    }

    private void BenchBeginSection()
    {
        if (!BenchMeasuring)
            return;
        _benchSectionStart = GC.GetAllocatedBytesForCurrentThread();
        _benchClock.Restart();
    }

    private void BenchEndSection(double[] times, ref long allocated)
    {
        if (!BenchMeasuring)
            return;
        times[_benchFrame - BenchWarmUpFrames] = _benchClock.Elapsed.TotalMilliseconds;
        allocated += GC.GetAllocatedBytesForCurrentThread() - _benchSectionStart;
    }

    /// <summary>Called once per drawn frame: counts the frame, and prints the results and exits after the last one.</summary>
    private void BenchFrameDone()
    {
        if (!Benchmarking)
            return;
        if (_benchFrame == BenchWarmUpFrames)
            (_benchGen0, _benchGen1, _benchGen2) = (GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2));
        if (BenchMeasuring)
        {
            _benchMaxDrawCalls = Math.Max(_benchMaxDrawCalls, _drawCalls);
            _benchMaxLayers = Math.Max(_benchMaxLayers, _layersDrawn);
        }

        if (++_benchFrame < BenchWarmUpFrames + BenchFrames)
            return;

        var viewport = GraphicsDevice.Viewport;
        Console.WriteLine($"Bench: {string.Join(", ", _slots.Select(s => s.Label))}");
        Console.WriteLine($"  {BenchFrames} frames after {BenchWarmUpFrames} warm-up, {viewport.Width}x{viewport.Height}, texture scale {_library.TextureScale}, " +
                          $"{_slots.Sum(s => s.Instance.Animations.Count)} animations looping, physics on, parameters swept");
        Console.WriteLine($"  update  {Summary(_benchUpdate)}");
        Console.WriteLine($"          per puppet, whole run: {string.Join(", ", _slots.Select(s => $"{s.Label} {s.UpdateMilliseconds / (_benchFrame + 1):F3}"))}");
        Console.WriteLine($"  draw    {Summary(_benchDraw)}");
        Console.WriteLine($"  total   {Summary([.. _benchUpdate.Zip(_benchDraw, (u, d) => u + d)])}");
        Console.WriteLine($"  allocated {_benchAllocatedUpdate / (double)BenchFrames:F0} B/frame in update, {_benchAllocatedDraw / (double)BenchFrames:F0} in draw; collections gen0 {GC.CollectionCount(0) - _benchGen0}, " +
                          $"gen1 {GC.CollectionCount(1) - _benchGen1}, gen2 {GC.CollectionCount(2) - _benchGen2}");
        Console.WriteLine($"  up to {_benchMaxDrawCalls} draw calls and {_benchMaxLayers} layers per frame (last frame: {string.Join(", ", _slots.Select(s => $"{s.Label} {s.LastDrawCalls}"))})");
        Console.WriteLine($"  GPU textures: {GpuTextureBytes() / 1048576.0:F0} MB ({string.Join(", ", _slots.Select(s => s.Source).Distinct().Select(s => $"{s.DisplayName} {GpuTextureBytes(s.Textures) / 1048576.0:F0}"))})");
        Exit();
    }

    private static string Summary(double[] times)
    {
        var sorted = times.Order().ToArray();
        return $"mean {times.Average():F3} ms, median {sorted[sorted.Length / 2]:F3}, p95 {sorted[(int)(sorted.Length * 0.95)]:F3}, max {sorted[^1]:F3}";
    }

    private long GpuTextureBytes() => _slots.Select(s => s.Source).Distinct().Sum(s => GpuTextureBytes(s.Textures));

    /// <summary>RGBA bytes of a puppet's GPU textures, with a third more for mipmaps.</summary>
    internal static long GpuTextureBytes(PuppetTextures textures) =>
        textures.GpuTextures.Sum(t => (long)t.Width * t.Height * 4 * (t.LevelCount > 1 ? 4 : 3) / 3);
}
