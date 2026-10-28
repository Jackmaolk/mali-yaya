using System.Collections;
using System.IO;
using System.Resources;
using System.Security.Cryptography;
using System.Reflection;
using Xunit;

namespace CodexUsageWidget.Tests;

public sealed class IconResourceTests
{
    [Fact]
    public void WindowIconIsEmbeddedAndMatchesReviewedArtwork()
    {
        var assembly = typeof(App).Assembly;
        using var resourceStream = assembly.GetManifestResourceStream("CodexUsageWidget.g.resources");
        Assert.NotNull(resourceStream);
        using var resources = new ResourceReader(resourceStream);
        var entry = resources.Cast<DictionaryEntry>().Single(pair => (string)pair.Key == "assets/mali-yaya.ico");
        using var icon = Assert.IsAssignableFrom<Stream>(entry.Value);
        using var data = new MemoryStream();
        icon.CopyTo(data);
        var bytes = data.ToArray();
        Assert.Equal("4c03ec2a9d49defebe73a322ec837d1a3c22780a6857d28c4e3a223a989fc999",
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
        Assert.Equal(1, BitConverter.ToUInt16(bytes, 2));
        Assert.Equal(9, BitConverter.ToUInt16(bytes, 4));
        Assert.Contains("Mali Yaya", assembly.GetCustomAttribute<AssemblyProductAttribute>()!.Product);
    }
}
