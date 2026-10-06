using System;
using System.IO;
using System.Linq;
using Ludots.Core.CrowdSimulation.World;
using NUnit.Framework;

namespace CrowdSimulationTests;

/// <summary>.navsurface v1：往返一致性 + 格式门禁（magic / 版本 / 长度 / 越界）。</summary>
[TestFixture]
public class NavSurfaceAssetTests
{
    private static NavSurfaceAsset Sample()
    {
        var cells = new byte[256 * 256];
        var rng = new System.Random(1337);
        rng.NextBytes(cells);
        for (int i = 0; i < cells.Length; i++) cells[i] = (byte)(cells[i] % 5);

        return new NavSurfaceAsset
        {
            CellsX = 256,
            CellsY = 256,
            CellSizeCm = 6250,
            TerrainTypeIds = new[] { "water", "shore", "land", "mountain", "cliff" },
            TerrainCells = cells,
            JumpCandidates = new[]
            {
                new NavSurfaceJumpCandidate { FromX = 10, FromY = 20, ToX = 12, ToY = 20, DropCm = 1500, LengthCells = 2f },
                new NavSurfaceJumpCandidate { FromX = 100, FromY = 100, ToX = 100, ToY = 106, DropCm = -2000, LengthCells = 6f },
            },
        };
    }

    private static byte[] Serialize(NavSurfaceAsset asset)
    {
        using var ms = new MemoryStream();
        NavSurfaceAsset.Write(asset, ms);
        return ms.ToArray();
    }

    [Test]
    public void RoundTrip_PreservesEveryField()
    {
        var asset = Sample();
        var bytes = Serialize(asset);
        var read = NavSurfaceAsset.Read(new MemoryStream(bytes));

        Assert.That(read.CellsX, Is.EqualTo(asset.CellsX));
        Assert.That(read.CellsY, Is.EqualTo(asset.CellsY));
        Assert.That(read.CellSizeCm, Is.EqualTo(asset.CellSizeCm));
        Assert.That(read.TerrainTypeIds, Is.EqualTo(asset.TerrainTypeIds));
        Assert.That(read.TerrainCells, Is.EqualTo(asset.TerrainCells));
        Assert.That(read.JumpCandidates.Length, Is.EqualTo(2));
        Assert.That(read.JumpCandidates[0].FromX, Is.EqualTo(10));
        Assert.That(read.JumpCandidates[0].DropCm, Is.EqualTo(1500));
        Assert.That(read.JumpCandidates[1].ToY, Is.EqualTo(106));
        Assert.That(read.JumpCandidates[1].LengthCells, Is.EqualTo(6f));
    }

    [Test]
    public void BadMagic_IsRejected()
    {
        var bytes = Serialize(Sample());
        bytes[0] ^= 0xFF;
        var ex = Assert.Throws<InvalidDataException>(() => NavSurfaceAsset.Read(new MemoryStream(bytes)));
        Assert.That(ex!.Message, Does.Contain("magic"));
    }

    [Test]
    public void UnsupportedVersion_IsRejected()
    {
        var bytes = Serialize(Sample());
        BitConverter.GetBytes((ushort)2).CopyTo(bytes, 4);
        var ex = Assert.Throws<InvalidDataException>(() => NavSurfaceAsset.Read(new MemoryStream(bytes)));
        Assert.That(ex!.Message, Does.Contain("version"));
        Assert.That(ex.Message, Does.Contain("无迁移"));
    }

    [Test]
    public void TruncatedFile_IsRejected()
    {
        var bytes = Serialize(Sample());
        var truncated = bytes.Take(bytes.Length - 100).ToArray();
        Assert.Throws<InvalidDataException>(() => NavSurfaceAsset.Read(new MemoryStream(truncated)));
    }

    [Test]
    public void TrailingBytes_AreRejected()
    {
        var bytes = Serialize(Sample()).Concat(new byte[] { 0, 1 }).ToArray();
        var ex = Assert.Throws<InvalidDataException>(() => NavSurfaceAsset.Read(new MemoryStream(bytes)));
        Assert.That(ex!.Message, Does.Contain("长度校验失败"));
    }

    [Test]
    public void CellValue_OutOfTypeRange_IsRejected()
    {
        var asset = Sample();
        asset.TerrainCells[42] = 200;
        Assert.Throws<InvalidDataException>(() => Serialize(asset));
    }

    [Test]
    public void DuplicateTerrainId_IsRejected()
    {
        var asset = Sample();
        var ids = asset.TerrainTypeIds.ToArray();
        ids[1] = ids[0];
        var broken = new NavSurfaceAsset
        {
            CellsX = asset.CellsX,
            CellsY = asset.CellsY,
            CellSizeCm = asset.CellSizeCm,
            TerrainTypeIds = ids,
            TerrainCells = asset.TerrainCells,
            JumpCandidates = asset.JumpCandidates,
        };
        Assert.Throws<InvalidDataException>(() => Serialize(broken));
    }

    [Test]
    public void JumpEndpoint_OutOfGrid_IsRejectedOnRead()
    {
        var asset = Sample();
        var jumps = asset.JumpCandidates.ToArray();
        jumps[0] = new NavSurfaceJumpCandidate { FromX = 300, FromY = 0, ToX = 1, ToY = 1, DropCm = 0, LengthCells = 1f };
        var broken = new NavSurfaceAsset
        {
            CellsX = asset.CellsX,
            CellsY = asset.CellsY,
            CellSizeCm = asset.CellSizeCm,
            TerrainTypeIds = asset.TerrainTypeIds,
            TerrainCells = asset.TerrainCells,
            JumpCandidates = jumps,
        };
        var bytes = Serialize(broken); // 写出层不查格界（写入者是工具链），读取层必须拦截
        var ex = Assert.Throws<InvalidDataException>(() => NavSurfaceAsset.Read(new MemoryStream(bytes)));
        Assert.That(ex!.Message, Does.Contain("端点越界"));
    }

    [Test]
    public void Contract_TerrainTableMustMatchConfigOrder()
    {
        var runtime = TestDefaults.Assemble();
        var asset = Sample();
        Assert.DoesNotThrow(() => NavSurfaceContract.Validate(asset, runtime));

        var reordered = new NavSurfaceAsset
        {
            CellsX = asset.CellsX,
            CellsY = asset.CellsY,
            CellSizeCm = asset.CellSizeCm,
            TerrainTypeIds = asset.TerrainTypeIds.Reverse().ToArray(),
            TerrainCells = asset.TerrainCells,
            JumpCandidates = asset.JumpCandidates,
        };
        var ex = Assert.Throws<InvalidOperationException>(() => NavSurfaceContract.Validate(reordered, runtime));
        Assert.That(ex!.Message, Does.Contain("顺序必须相同"));
    }

    [Test]
    public void Contract_SizeMustMatchNavGrid()
    {
        var runtime = TestDefaults.Assemble();
        var asset = Sample();
        var wrong = new NavSurfaceAsset
        {
            CellsX = 128,
            CellsY = 128,
            CellSizeCm = asset.CellSizeCm,
            TerrainTypeIds = asset.TerrainTypeIds,
            TerrainCells = new byte[128 * 128],
            JumpCandidates = Array.Empty<NavSurfaceJumpCandidate>(),
        };
        var ex = Assert.Throws<InvalidOperationException>(() => NavSurfaceContract.Validate(wrong, runtime));
        Assert.That(ex!.Message, Does.Contain("导航网格"));
    }
}
