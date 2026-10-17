using System.Text;

namespace PaperDoll.Tests;

public class ContainerTests
{
    [Fact]
    public void MinimalPuppetLoads()
    {
        var puppet = TestData.Load(TestData.Puppet());

        Assert.Equal("Root", puppet.Root.Name);
        Assert.Equal(NodeKind.Node, puppet.Root.Kind);
        Assert.Single(puppet.Nodes);
        Assert.Empty(puppet.Textures);
        Assert.Empty(puppet.Extensions);
        Assert.Equal("Test", puppet.Meta.Name);
        Assert.Equal(new PuppetPhysicsSettings(1000f, 9.8f), puppet.Physics);
        Assert.Empty(puppet.Issues);
    }

    [Fact]
    public void Inp2IsRejectedWithAClearError()
    {
        var data = "TRNSRTS2"u8.ToArray().Concat(new byte[32]).ToArray();

        var ex = Assert.Throws<PuppetFormatException>(() => Puppet.Load(data));
        Assert.Contains("INP2", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void WrongMagicIsRejected()
    {
        Assert.Throws<PuppetFormatException>(() => Puppet.Load(Encoding.UTF8.GetBytes("{\"meta\":{}}")));
        Assert.Throws<PuppetFormatException>(() => Puppet.Load(Array.Empty<byte>()));
    }

    [Fact]
    public void TruncatedFileIsRejected()
    {
        var data = TestData.Inp(TestData.Puppet(), [(0, TestData.Png(2, 2, TestData.Pixels2x2))]);

        var ex = Assert.Throws<PuppetFormatException>(() => Puppet.Load(data.AsMemory(0, data.Length - 5)));
        Assert.Contains("ends early", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingTextureSectionIsRejected()
    {
        var data = TestData.Inp(TestData.Puppet());

        Assert.Throws<PuppetFormatException>(() => Puppet.Load(data.AsMemory(0, data.Length - 12)));
    }

    [Fact]
    public void InvalidJsonIsRejected()
    {
        Assert.Throws<PuppetFormatException>(() => Puppet.Load(TestData.Inp("{ \"meta\": ")));
    }

    [Fact]
    public void ExtensionsAreKeptAsBlobs()
    {
        var data = TestData.Inp(TestData.Puppet(), extensions: [("com.example.one", [1, 2, 3]), ("com.example.two", [])]);

        var puppet = Puppet.Load(data);

        Assert.Equal([1, 2, 3], puppet.Extensions["com.example.one"].ToArray());
        Assert.True(puppet.Extensions["com.example.two"].IsEmpty);
    }

    [Fact]
    public void TrailingBytesAreReported()
    {
        var puppet = Puppet.Load(TestData.Inp(TestData.Puppet(), trailing: [9, 9, 9]));

        Assert.Contains(puppet.Issues, i => i.Message.Contains("3 bytes", StringComparison.Ordinal));
    }

    [Fact]
    public void TextureSlotsKeepTheirEncodingAndSize()
    {
        var png = TestData.Png(2, 2, TestData.Pixels2x2);
        var puppet = TestData.Load(TestData.Puppet(), [(0, png), (1, []), (2, [1, 2, 3, 4]), (7, [1])]);

        Assert.Equal(4, puppet.Textures.Count);
        Assert.Equal(TextureEncoding.Png, puppet.Textures[0].Encoding);
        Assert.Equal((2, 2), (puppet.Textures[0].Width, puppet.Textures[0].Height));
        Assert.True(puppet.Textures[0].CanDecode);

        Assert.True(puppet.Textures[1].IsEmpty);
        Assert.False(puppet.Textures[1].CanDecode);

        Assert.Equal(TextureEncoding.Bc7, puppet.Textures[2].Encoding);
        Assert.False(puppet.Textures[2].CanDecode);
        Assert.Throws<NotSupportedException>(() => puppet.Textures[2].Decode());

        Assert.Null(puppet.Textures[3].Encoding);
        Assert.Contains(puppet.Issues, i => i.Kind == LoadIssueKind.UnsupportedFeature && i.Message.Contains("BC7", StringComparison.Ordinal));
        Assert.Contains(puppet.Issues, i => i.Kind == LoadIssueKind.UnsupportedFeature && i.Message.Contains("encoding 7", StringComparison.Ordinal));
    }

    [Fact]
    public void LoadsFromStreamAndFile()
    {
        var data = TestData.Inp(TestData.Puppet());
        using var stream = new MemoryStream(data);
        Assert.Equal("Root", Puppet.Load(stream).Root.Name);

        var path = Path.Combine(Path.GetTempPath(), $"paperdoll-{Guid.NewGuid():N}.inx");
        try
        {
            File.WriteAllBytes(path, data);
            Assert.Equal("Root", Puppet.Load(path).Root.Name);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
