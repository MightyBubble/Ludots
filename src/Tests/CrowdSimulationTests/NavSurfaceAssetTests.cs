using System;
using System.IO;
using System.Linq;
using Ludots.Core.CrowdSimulation.Config;
using Ludots.Core.CrowdSimulation.World;
using Ludots.Core.Presentation.Terrain;
using Ludots.Platform.Abstractions;
using NUnit.Framework;

namespace CrowdSimulationTests;

/// <summary>.navsurface v2：往返一致性 + 格式门禁（magic / 版本 / 长度 / 越界 / 候选位移）+ 和配置的链接种类表对照。烘焙输入指纹见 <see cref="NavSurfaceBakeInputsStalenessTests"/>。</summary>
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
            NavCellsX = 256,
            NavCellsY = 256,
            NavCellSizeCm = 6250,
            TerrainTypeIds = new[] { "water", "shore", "land", "mountain", "cliff" },
            LinkKindIds = new[] { "jump" },
            BakeInputsDigest = 0xA1B2C3D4u,
            TerrainCells = cells,
            LinkCandidates = new[]
            {
                new NavSurfaceLinkCandidate { Steps = 2, FromX = 10, FromY = 20, ToX = 12, ToY = 20, DropCm = 1500 },
                new NavSurfaceLinkCandidate { Steps = 3, Diagonal = true, FromX = 100, FromY = 100, ToX = 97, ToY = 103, DropCm = 2000 },
            },
        };
    }

    private static NavSurfaceAsset With(NavSurfaceAsset asset, NavSurfaceLinkCandidate[]? candidates = null, string[]? linkKinds = null, uint? digest = null, string[]? terrainTypes = null)
        => new()
        {
            NavCellsX = asset.NavCellsX,
            NavCellsY = asset.NavCellsY,
            NavCellSizeCm = asset.NavCellSizeCm,
            TerrainTypeIds = terrainTypes ?? asset.TerrainTypeIds,
            LinkKindIds = linkKinds ?? asset.LinkKindIds,
            BakeInputsDigest = digest ?? asset.BakeInputsDigest,
            TerrainCells = asset.TerrainCells,
            LinkCandidates = candidates ?? asset.LinkCandidates,
        };

    private static NavSurfaceAsset MatchingConfig(CrowdSimulationRuntimeConfig runtime)
        => With(
            Sample(),
            candidates: Array.Empty<NavSurfaceLinkCandidate>(),
            linkKinds: runtime.LinkCandidateBakes.Select(b => b.KindId).ToArray());

    private static int CandidateOffset(byte[] bytes, int index, int count) => bytes.Length - (count - index) * NavSurfaceAsset.LinkCandidateRecordBytes;

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

        Assert.That(read.NavCellsX, Is.EqualTo(asset.NavCellsX));
        Assert.That(read.NavCellsY, Is.EqualTo(asset.NavCellsY));
        Assert.That(read.NavCellSizeCm, Is.EqualTo(asset.NavCellSizeCm));
        Assert.That(read.TerrainTypeIds, Is.EqualTo(asset.TerrainTypeIds));
        Assert.That(read.TerrainCells, Is.EqualTo(asset.TerrainCells));
        Assert.That(read.LinkKindIds, Is.EqualTo(asset.LinkKindIds));
        Assert.That(read.BakeInputsDigest, Is.EqualTo(0xA1B2C3D4u));
        Assert.That(read.LinkCandidates, Is.EqualTo(asset.LinkCandidates));
        Assert.That(read.LinkCandidates[0].LengthCells, Is.EqualTo(2f));
        Assert.That(read.LinkCandidates[1].LengthCells, Is.EqualTo((float)(3 * Math.Sqrt(2.0))));
    }

    [Test]
    public void Header_FollowsV2Layout()
    {
        var bytes = Serialize(Sample());
        Assert.That(BitConverter.ToUInt16(bytes, 4), Is.EqualTo((ushort)2));
        Assert.That(BitConverter.ToUInt16(bytes, 6), Is.EqualTo((ushort)32));
        Assert.That(BitConverter.ToUInt16(bytes, 20), Is.EqualTo((ushort)5));
        Assert.That(BitConverter.ToUInt16(bytes, 22), Is.EqualTo((ushort)1));
        Assert.That(BitConverter.ToInt32(bytes, 24), Is.EqualTo(2));
        Assert.That(BitConverter.ToUInt32(bytes, 28), Is.EqualTo(0xA1B2C3D4u));
        int second = CandidateOffset(bytes, 1, 2);
        Assert.That(bytes[second], Is.EqualTo(0), "kindIndex");
        Assert.That(bytes[second + 1], Is.EqualTo(NavSurfaceAsset.DiagonalFlag), "flags");
        Assert.That(BitConverter.ToUInt16(bytes, second + 2), Is.EqualTo((ushort)3), "steps");
        Assert.That(BitConverter.ToInt32(bytes, second + 12), Is.EqualTo(2000), "dropCm");
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
    public void V1_IsRejectedWithRebakeHint()
    {
        var bytes = Serialize(Sample());
        BitConverter.GetBytes((ushort)1).CopyTo(bytes, 4);
        var ex = Assert.Throws<InvalidDataException>(() => NavSurfaceAsset.Read(new MemoryStream(bytes)));
        Assert.That(ex!.Message, Does.Contain("version = 1"));
        Assert.That(ex.Message, Does.Contain("重烘"));
    }

    [Test]
    public void HeaderBytes_MustBeExactly32()
    {
        var longer = Serialize(Sample());
        BitConverter.GetBytes((ushort)33).CopyTo(longer, 6);
        var longHeader = Assert.Throws<InvalidDataException>(() => NavSurfaceAsset.Read(new MemoryStream(longer)));
        Assert.That(longHeader!.Message, Does.Contain("headerBytes"));
        Assert.That(longHeader.Message, Does.Contain("32"));

        var shorter = Serialize(Sample());
        BitConverter.GetBytes((ushort)16).CopyTo(shorter, 6);
        var shortHeader = Assert.Throws<InvalidDataException>(() => NavSurfaceAsset.Read(new MemoryStream(shorter)));
        Assert.That(shortHeader!.Message, Does.Contain("headerBytes"));
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
        Assert.Throws<InvalidDataException>(() => Serialize(With(asset, terrainTypes: ids)));
        Assert.Throws<InvalidDataException>(() => Serialize(With(asset, linkKinds: new[] { "jump", "jump" })));
    }

    [Test]
    public void CandidateEndpoint_OutOfGrid_IsRejected()
    {
        var bytes = Serialize(Sample());
        BitConverter.GetBytes((ushort)300).CopyTo(bytes, CandidateOffset(bytes, 0, 2) + 4);
        var ex = Assert.Throws<InvalidDataException>(() => NavSurfaceAsset.Read(new MemoryStream(bytes)));
        Assert.That(ex!.Message, Does.Contain("端点越界"));

        var asset = Sample();
        var outside = new[] { new NavSurfaceLinkCandidate { Steps = 1, FromX = 255, FromY = 0, ToX = 256, ToY = 0 } };
        Assert.Throws<InvalidDataException>(() => Serialize(With(asset, candidates: outside)));
    }

    [TestCase(1, 0x02, "flags")]
    [TestCase(2, 0, "steps = 0")]
    [TestCase(8, 13, "位移 (3, 0)")]
    public void CandidateRecord_FieldRules_AreCheckedOnRead(int fieldOffset, int value, string expected)
    {
        var bytes = Serialize(Sample());
        int at = CandidateOffset(bytes, 0, 2) + fieldOffset;
        if (fieldOffset == 1)
        {
            bytes[at] = (byte)value;
        }
        else
        {
            BitConverter.GetBytes((ushort)value).CopyTo(bytes, at);
        }

        var ex = Assert.Throws<InvalidDataException>(() => NavSurfaceAsset.Read(new MemoryStream(bytes)));
        Assert.That(ex!.Message, Does.Contain(expected));
    }

    [Test]
    public void CandidateRecord_DiagonalFlagMustMatchDisplacement()
    {
        var bytes = Serialize(Sample());
        bytes[CandidateOffset(bytes, 0, 2) + 1] = NavSurfaceAsset.DiagonalFlag;
        var straightMarkedDiagonal = Assert.Throws<InvalidDataException>(() => NavSurfaceAsset.Read(new MemoryStream(bytes)));
        Assert.That(straightMarkedDiagonal!.Message, Does.Contain("斜向 = True"));

        bytes = Serialize(Sample());
        bytes[CandidateOffset(bytes, 1, 2) + 1] = 0;
        var diagonalMarkedStraight = Assert.Throws<InvalidDataException>(() => NavSurfaceAsset.Read(new MemoryStream(bytes)));
        Assert.That(diagonalMarkedStraight!.Message, Does.Contain("斜向 = False"));
    }

    [Test]
    public void CandidateRecord_KindAndDrop_AreChecked()
    {
        var bytes = Serialize(Sample());
        bytes[CandidateOffset(bytes, 1, 2)] = 1;
        var kind = Assert.Throws<InvalidDataException>(() => NavSurfaceAsset.Read(new MemoryStream(bytes)));
        Assert.That(kind!.Message, Does.Contain("kindIndex = 1"));

        bytes = Serialize(Sample());
        BitConverter.GetBytes(-1).CopyTo(bytes, CandidateOffset(bytes, 0, 2) + 12);
        var drop = Assert.Throws<InvalidDataException>(() => NavSurfaceAsset.Read(new MemoryStream(bytes)));
        Assert.That(drop!.Message, Does.Contain("dropCm = -1"));

        var asset = Sample();
        var twoKinds = With(asset, linkKinds: new[] { "jump", "climb" }, candidates: new[]
        {
            new NavSurfaceLinkCandidate { KindIndex = 1, Steps = 1, FromX = 0, FromY = 0, ToX = 1, ToY = 0 },
            new NavSurfaceLinkCandidate { KindIndex = 0, Steps = 1, FromX = 0, FromY = 0, ToX = 1, ToY = 0 },
        });
        var order = Assert.Throws<InvalidDataException>(() => Serialize(twoKinds));
        Assert.That(order!.Message, Does.Contain("升序"));
    }

    [Test]
    public void Contract_TerrainTableMustMatchConfigOrder()
    {
        var runtime = TestDefaults.Assemble();
        var asset = MatchingConfig(runtime);
        Assert.DoesNotThrow(() => NavSurfaceContract.Validate(asset, runtime));

        var reordered = With(asset, terrainTypes: asset.TerrainTypeIds.Reverse().ToArray());
        var ex = Assert.Throws<InvalidOperationException>(() => NavSurfaceContract.Validate(reordered, runtime));
        Assert.That(ex!.Message, Does.Contain("顺序必须相同"));
    }

    [Test]
    public void Contract_LinkKindTableMustMatchConfig()
    {
        var runtime = TestDefaults.Assemble();
        var asset = MatchingConfig(runtime);

        var extraKind = With(asset, linkKinds: asset.LinkKindIds.Append("climb").ToArray());
        var kinds = Assert.Throws<InvalidOperationException>(() => NavSurfaceContract.Validate(extraKind, runtime));
        Assert.That(kinds!.Message, Does.Contain("链接种类表"));
        Assert.That(kinds.Message, Does.Contain("重烘"));
    }

    [Test]
    public void BakeInputsDigest_WithoutLinks_HashesOnlyNavCellSize()
    {
        uint expected = 2166136261u;
        foreach (byte b in BitConverter.GetBytes(6250))
        {
            expected = (expected ^ b) * 16777619u;
        }

        Assert.That(NavSurfaceContract.BakeInputsDigestWithoutLinks(6250), Is.EqualTo(expected));
        Assert.That(expected, Is.EqualTo(0x8227BA97u));
    }

    [Test]
    public void Contract_SizeMustMatchNavGrid()
    {
        var runtime = TestDefaults.Assemble();
        var asset = Sample();
        var wrong = new NavSurfaceAsset
        {
            NavCellsX = 128,
            NavCellsY = 128,
            NavCellSizeCm = asset.NavCellSizeCm,
            TerrainTypeIds = asset.TerrainTypeIds,
            LinkKindIds = asset.LinkKindIds,
            BakeInputsDigest = asset.BakeInputsDigest,
            TerrainCells = new byte[128 * 128],
            LinkCandidates = Array.Empty<NavSurfaceLinkCandidate>(),
        };
        int boardCm = runtime.NavCellsPerSide * runtime.NavCellSizeCm;
        var height = ContinuousHeightmapAsset.CreateSingleLayerFromRawUInt16(
            new WorldAabbCm(0, 0, boardCm, boardCm),
            runtime.NavCellsPerSide,
            runtime.NavCellsPerSide,
            new ushort[runtime.NavCellsPerSide * runtime.NavCellsPerSide],
            ContinuousHeightSampleScale.IdentityCentimeters);
        const string surfacePath = "assets/terrain/fixture.navsurface";
        var ex = Assert.Throws<InvalidOperationException>(() => NavSurfaceContract.ValidateWorldSize(
            boardCm, boardCm, runtime.NavCellsPerSide, runtime.NavCellSizeCm,
            wrong, surfacePath, height, "assets/terrain/fixture.height"));
        Assert.That(ex!.Message, Does.StartWith(surfacePath));
        Assert.That(ex.Message, Does.Contain("128×128"));
    }
}
