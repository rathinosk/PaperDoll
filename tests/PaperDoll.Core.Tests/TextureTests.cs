namespace PaperDoll.Tests;

public class TextureTests
{
    [Fact]
    public void PngDecodesToRgba()
    {
        var puppet = TestData.Load(TestData.Puppet(), [(0, TestData.Png(2, 2, TestData.Pixels2x2))]);

        var image = puppet.Textures[0].Decode();

        Assert.Equal(2, image.Width);
        Assert.Equal(2, image.Height);
        Assert.Equal(TestData.Pixels2x2, image.Rgba);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TgaDecodesToRgba(bool rle)
    {
        var puppet = TestData.Load(TestData.Puppet(), [(1, TestData.Tga(2, 2, TestData.Pixels2x2, rle))]);

        var texture = puppet.Textures[0];
        var image = texture.Decode();

        Assert.Equal(TextureEncoding.Tga, texture.Encoding);
        Assert.Equal((2, 2), (texture.Width, texture.Height));
        Assert.Equal(TestData.Pixels2x2, image.Rgba);
    }

    [Fact]
    public void CorruptImageReportsAndCannotDecode()
    {
        var puppet = TestData.Load(TestData.Puppet(), [(0, [0x89, (byte)'P', (byte)'N', (byte)'G', 1, 2, 3])]);

        Assert.False(puppet.Textures[0].CanDecode);
        Assert.Contains(puppet.Issues, i => i.Kind == LoadIssueKind.InvalidValue && i.Location == "Texture 0");
    }
}
