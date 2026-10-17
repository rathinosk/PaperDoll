using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PaperDoll.Snapshots;

#if MONOGAME
namespace PaperDoll.MonoGame.Tests;
#else
namespace PaperDoll.Fna.Tests;
#endif

/// <summary>
/// Once warmed up, updating and drawing a puppet (physics on, animations playing, parameters moving)
/// allocates no managed memory.
/// </summary>
[Collection(GraphicsCollection.Name)]
public sealed class AllocationTests(GraphicsFixture graphics)
{
    public static TheoryData<string> Samples => new()
    {
        "99days-samples/claire.inx", "inochi3d-samples/Midori.inx", "inochi3d-samples/Yumi/Yumi.inx", "mannequin-samples/Lena.inx",
    };

    [Theory]
    [MemberData(nameof(Samples))]
    public void UpdatingAndDrawingAllocatesNothing(string file)
    {
        var root = SampleLocator.FindSamplesRoot();
        var path = root is null ? null : Path.Combine(root, file);
        if (path is null || !File.Exists(path))
            Assert.Skip($"Sample puppet '{file}' is not present in .samples/.");

        var device = graphics.Require();
        const int size = 512;
        var puppet = Puppet.Load(path);
        using var target = new RenderTarget2D(device, size, size, false, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
        using var textures = new PuppetTextures(device, puppet);
        using var renderer = new PuppetRenderer(device);
        var instance = puppet.CreateInstance();
        foreach (var playback in instance.Animations)
            playback.Play(loop: true);
        var bounds = instance.Bounds;
        var scale = size / Math.Max(bounds.Size.X, bounds.Size.Y);
        var transform = Matrix.CreateTranslation(-bounds.Center.X, -bounds.Center.Y, 0) * Matrix.CreateScale(scale)
                        * Matrix.CreateTranslation(size / 2f, size / 2f, 0);

        device.SetRenderTarget(target);
        for (var frame = 0; frame < 30; frame++)
            Step(frame);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var frame = 30; frame < 90; frame++)
            Step(frame);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        device.SetRenderTarget(null);

        Assert.Equal(0, allocated);

        void Step(int frame)
        {
            // Sweep every parameter physics does not drive, as a host animating the puppet would.
            for (var i = 0; i < puppet.Parameters.Count; i++)
            {
                var parameter = puppet.Parameters[i];
                if (parameter.PhysicsDriver is not null)
                    continue;
                var t = 0.5f + 0.5f * MathF.Sin(frame * 0.2f + parameter.Index);
                instance.SetParameterValue(parameter, parameter.Min + (parameter.Max - parameter.Min) * t);
            }

            instance.Update(1f / 60);
            device.Clear(Color.Transparent);
            renderer.Draw(instance, textures, transform);
        }
    }
}
