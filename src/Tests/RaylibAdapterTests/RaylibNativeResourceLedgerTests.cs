using Ludots.Raylib.Render;
using NUnit.Framework;
using Raylib_cs;

namespace Ludots.Tests.RaylibAdapter;

[TestFixture]
public sealed class RaylibNativeResourceLedgerTests
{
    [SetUp]
    public void SetUp()
    {
        RaylibNativeResourceLedger.Reset();
    }

    [TearDown]
    public void TearDown()
    {
        RaylibNativeResourceLedger.Reset();
    }

    [Test]
    public void TrackThenUntrack_AllKinds_ReturnsToZeroResidency()
    {
        RaylibNativeResourceLedger.Track(RaylibNativeResourceKind.Texture, 7, 128);
        RaylibNativeResourceLedger.Track(RaylibNativeResourceKind.Model, 9, 512);
        RaylibNativeResourceLedger.Track(RaylibNativeResourceKind.Shader, 3, 0);

        RaylibNativeResourceSnapshot loaded = RaylibNativeResourceLedger.Snapshot();
        Assert.That(loaded.ResidentBytes, Is.EqualTo(640));
        Assert.That(loaded.OutstandingCount, Is.EqualTo(3));
        Assert.That(loaded.OutstandingByKind[(int)RaylibNativeResourceKind.Texture], Is.EqualTo(1));
        Assert.That(loaded.OutstandingByKind[(int)RaylibNativeResourceKind.Model], Is.EqualTo(1));
        Assert.That(loaded.OutstandingByKind[(int)RaylibNativeResourceKind.Shader], Is.EqualTo(1));
        Assert.That(loaded.LifetimeTracked, Is.EqualTo(3));

        RaylibNativeResourceLedger.Untrack(RaylibNativeResourceKind.Texture, 7);
        RaylibNativeResourceLedger.Untrack(RaylibNativeResourceKind.Model, 9);
        RaylibNativeResourceLedger.Untrack(RaylibNativeResourceKind.Shader, 3);

        RaylibNativeResourceSnapshot unloaded = RaylibNativeResourceLedger.Snapshot();
        Assert.That(unloaded.ResidentBytes, Is.EqualTo(0));
        Assert.That(unloaded.OutstandingCount, Is.EqualTo(0));
        Assert.That(unloaded.LifetimeUntracked, Is.EqualTo(3));
        Assert.That(unloaded.UnknownUntrackCount, Is.EqualTo(0));
        Assert.That(unloaded.RetrackedCount, Is.EqualTo(0));
    }

    [Test]
    public void SameIdentityDifferentKind_TracksIndependently()
    {
        RaylibNativeResourceLedger.Track(RaylibNativeResourceKind.Texture, 42, 100);
        RaylibNativeResourceLedger.Track(RaylibNativeResourceKind.Mesh, 42, 50);

        RaylibNativeResourceLedger.Untrack(RaylibNativeResourceKind.Texture, 42);

        RaylibNativeResourceSnapshot snapshot = RaylibNativeResourceLedger.Snapshot();
        Assert.That(snapshot.ResidentBytes, Is.EqualTo(50));
        Assert.That(snapshot.OutstandingCount, Is.EqualTo(1));
        Assert.That(snapshot.OutstandingByKind[(int)RaylibNativeResourceKind.Mesh], Is.EqualTo(1));
        Assert.That(snapshot.UnknownUntrackCount, Is.EqualTo(0));
    }

    [Test]
    public void UntrackUnknownIdentity_IncrementsMismatchCounter_WithoutDisturbingResidency()
    {
        RaylibNativeResourceLedger.Track(RaylibNativeResourceKind.Texture, 7, 128);
        RaylibNativeResourceLedger.Untrack(RaylibNativeResourceKind.Texture, 999);

        RaylibNativeResourceSnapshot snapshot = RaylibNativeResourceLedger.Snapshot();
        Assert.That(snapshot.UnknownUntrackCount, Is.EqualTo(1));
        Assert.That(snapshot.ResidentBytes, Is.EqualTo(128));
        Assert.That(snapshot.OutstandingCount, Is.EqualTo(1));
    }

    [Test]
    public void RetrackLiveIdentity_ReplacesBytesAndCountsRetrack()
    {
        RaylibNativeResourceLedger.Track(RaylibNativeResourceKind.Texture, 7, 128);
        RaylibNativeResourceLedger.Track(RaylibNativeResourceKind.Texture, 7, 256);

        RaylibNativeResourceSnapshot snapshot = RaylibNativeResourceLedger.Snapshot();
        Assert.That(snapshot.RetrackedCount, Is.EqualTo(1));
        Assert.That(snapshot.ResidentBytes, Is.EqualTo(256));
        Assert.That(snapshot.OutstandingCount, Is.EqualTo(1));
        Assert.That(snapshot.LifetimeTracked, Is.EqualTo(1));
    }

    // raylib 5.5 raylib.h PixelFormat 1..24 的 bpp；12=R16G16B16(48)、13=R16G16B16A16(64)、19=ETC2_RGB(4) 是 5.0 后新增/移位项。
    [TestCase(1, 8L)]
    [TestCase(2, 16L)]
    [TestCase(3, 16L)]
    [TestCase(4, 24L)]
    [TestCase(5, 16L)]
    [TestCase(6, 16L)]
    [TestCase(7, 32L)]
    [TestCase(8, 32L)]
    [TestCase(9, 96L)]
    [TestCase(10, 128L)]
    [TestCase(11, 16L)]
    [TestCase(12, 48L)]
    [TestCase(13, 64L)]
    [TestCase(14, 4L)]
    [TestCase(15, 4L)]
    [TestCase(16, 8L)]
    [TestCase(17, 8L)]
    [TestCase(18, 4L)]
    [TestCase(19, 4L)]
    [TestCase(20, 8L)]
    [TestCase(21, 4L)]
    [TestCase(22, 4L)]
    [TestCase(23, 8L)]
    [TestCase(24, 2L)]
    public void EstimateTextureBytes_MatchesRaylib55PixelFormatNumbering(int format, long expectedBitsPerPixel)
    {
        long expectedBytes = 8 * 8 * expectedBitsPerPixel / 8;
        Assert.That(RaylibNativeResources.EstimateTextureBytes(Tex(8, 8, format, mipmaps: 1)), Is.EqualTo(expectedBytes));
    }

    [Test]
    public void EstimateTextureBytes_UnknownFormatFallsBackTo32bpp()
    {
        Assert.That(RaylibNativeResources.EstimateTextureBytes(Tex(4, 4, format: 99, mipmaps: 1)), Is.EqualTo(64));
    }

    [Test]
    public void EstimateTextureBytes_MipChainSumsQuarteredLevels()
    {
        Assert.That(RaylibNativeResources.EstimateTextureBytes(Tex(4, 4, format: 7, mipmaps: 2)), Is.EqualTo(64 + 16));
        Assert.That(RaylibNativeResources.EstimateTextureBytes(Tex(4, 4, format: 7, mipmaps: 3)), Is.EqualTo(64 + 16 + 4));
    }

    private static Texture2D Tex(int width, int height, int format, int mipmaps)
    {
        return new Texture2D { id = 0, width = width, height = height, mipmaps = mipmaps, format = format };
    }
}
