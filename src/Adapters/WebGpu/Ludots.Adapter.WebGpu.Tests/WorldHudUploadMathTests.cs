using System.Runtime.CompilerServices;
using Ludots.Client.WebGpu.Runtime;
using NUnit.Framework;

namespace Ludots.Adapter.WebGpu.Tests;

[TestFixture]
public sealed class WorldHudUploadMathTests
{
    [Test]
    public void DestinationByteOffset_MatchesInstanceStride()
    {
        Assert.That(
            WorldHudUploadMath.DestinationByteOffset(3, Unsafe.SizeOf<WebGpuWorldBarInstance>()),
            Is.EqualTo(3ul * (ulong)Unsafe.SizeOf<WebGpuWorldBarInstance>()));
        Assert.That(
            WorldHudUploadMath.DestinationByteOffset(7, Unsafe.SizeOf<WebGpuWorldHudAnchor>()),
            Is.EqualTo(7ul * (ulong)Unsafe.SizeOf<WebGpuWorldHudAnchor>()));
        Assert.That(
            WorldHudUploadMath.DestinationByteOffset(11, Unsafe.SizeOf<WebGpuWorldGlyphInstance>()),
            Is.EqualTo(11ul * (ulong)Unsafe.SizeOf<WebGpuWorldGlyphInstance>()));
    }

    [Test]
    public void WorldHudLayouts_AreStorageAndVertexFriendly()
    {
        Assert.That(Unsafe.SizeOf<WebGpuWorldBarInstance>() % 4, Is.EqualTo(0));
        Assert.That(Unsafe.SizeOf<WebGpuWorldHudAnchor>() % 16, Is.EqualTo(0));
        Assert.That(Unsafe.SizeOf<WebGpuWorldGlyphInstance>() % 4, Is.EqualTo(0));
        Assert.That(Unsafe.SizeOf<WebGpuCameraFrame>() % 16, Is.EqualTo(0));
        Assert.That(Unsafe.SizeOf<WebGpuWorldBarInstance>(), Is.EqualTo(64));
        Assert.That(Unsafe.SizeOf<WebGpuWorldHudAnchor>(), Is.EqualTo(16));
        Assert.That(Unsafe.SizeOf<WebGpuWorldGlyphInstance>(), Is.EqualTo(60));
        Assert.That(Unsafe.SizeOf<WebGpuCameraFrame>(), Is.EqualTo(80));
    }

    [Test]
    public void DestinationByteOffset_RejectsInvalidInputs()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => WorldHudUploadMath.DestinationByteOffset(-1, 16));
        Assert.Throws<ArgumentOutOfRangeException>(() => WorldHudUploadMath.DestinationByteOffset(0, 0));
    }
}
