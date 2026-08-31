using System;
using System.IO;
using Ludots.Raylib.Render;
using NUnit.Framework;

namespace PresentationTests.Rendering;

[TestFixture]
public sealed class RaylibPoseTexturePaletteContractTests
{
    [Test]
    public void PaletteDimensions_FitTheOpenGl33MinimumTextureContract()
    {
        Assert.That(RaylibPoseTexturePalette.BonePaletteTextureWidthTexels, Is.EqualTo(1024));
        Assert.That(RaylibPoseTexturePalette.BonePaletteTextureHeightTexels, Is.EqualTo(1024));
        Assert.That(RaylibPoseTexturePalette.BoneSlotsPerTextureRow, Is.EqualTo(256));
    }

    [Test]
    public void KayKitMultiMeshSlotCount_UsesThreePhysicalRowsPerPose()
    {
        const int kayKitBoneSlots = 615;

        (int boneSlotCapacity, int poseRowCapacity, int rowsPerPose) = RaylibPoseTexturePalette.ResolvePaletteCapacity(
            currentBoneSlotCapacity: RaylibPoseTexturePalette.BoneSlotsPerTextureRow,
            currentPoseRowCapacity: 64,
            minSlots: kayKitBoneSlots,
            minPoseRows: 64);

        Assert.That(boneSlotCapacity, Is.EqualTo(768));
        Assert.That(poseRowCapacity, Is.EqualTo(64));
        Assert.That(rowsPerPose, Is.EqualTo(3));
        Assert.That(RaylibPoseTexturePalette.GetPaletteHeightTexels(64, rowsPerPose: 3), Is.EqualTo(192));
        Assert.That(RaylibPoseTexturePalette.GetPoseTextureRow(poseRow: 2, rowsPerPose: 3), Is.EqualTo(6));
    }

    [TestCase(0, 255, 1, 0, 1020)]
    [TestCase(0, 256, 2, 1, 0)]
    [TestCase(0, 614, 3, 2, 408)]
    [TestCase(2, 614, 3, 8, 408)]
    public void BoneTexelAddress_ContinuesAcrossPhysicalTextureRows(
        int poseRow,
        int boneSlot,
        int rowsPerPose,
        int expectedRow,
        int expectedTexelX)
    {
        (int row, int texelX) = RaylibPoseTexturePalette.GetBoneTexelAddress(poseRow, boneSlot, rowsPerPose);

        Assert.That(row, Is.EqualTo(expectedRow));
        Assert.That(texelX, Is.EqualTo(expectedTexelX));
    }

    [Test]
    public void CapacityBeyondTheTwoDimensionalPalette_FailsExplicitly()
    {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => RaylibPoseTexturePalette.ResolvePaletteCapacity(
                currentBoneSlotCapacity: 256,
                currentPoseRowCapacity: 64,
                minSlots: 615,
                minPoseRows: 342))!;

        Assert.That(exception.Message, Does.Contain("exceeding the 1024x1024 2D palette capacity"));
    }

    [Test]
    public void LayoutPlanner_DoesNotLetPreviousFrameCapacityPoisonTheCurrentFrame()
    {
        (int highBoneCapacity, int lowPoseCapacity, _) = RaylibPoseTexturePalette.ResolvePaletteCapacity(
            currentBoneSlotCapacity: 256,
            currentPoseRowCapacity: 64,
            minSlots: 4000,
            minPoseRows: 1);
        (int lowBoneCapacity, int highPoseCapacity, int rowsPerPose) = RaylibPoseTexturePalette.ResolvePaletteCapacity(
            highBoneCapacity,
            lowPoseCapacity,
            minSlots: 100,
            minPoseRows: 100);

        Assert.That(highBoneCapacity, Is.EqualTo(4096));
        Assert.That(lowPoseCapacity, Is.EqualTo(64));
        Assert.That(lowBoneCapacity, Is.EqualTo(256));
        Assert.That(highPoseCapacity, Is.EqualTo(128));
        Assert.That(rowsPerPose, Is.EqualTo(1));
    }

    [Test]
    public void LayoutPlanner_StableWorkloadKeepsTheExistingAllocation()
    {
        (int boneSlotCapacity, int poseRowCapacity, int rowsPerPose) = RaylibPoseTexturePalette.ResolvePaletteCapacity(
            currentBoneSlotCapacity: 768,
            currentPoseRowCapacity: 64,
            minSlots: 615,
            minPoseRows: 32);

        Assert.That((boneSlotCapacity, poseRowCapacity, rowsPerPose), Is.EqualTo((768, 64, 3)));
    }

    [Test]
    public void BoneAddressing_WarmPathAllocatesNothing()
    {
        int checksum = 0;
        for (int i = 0; i < 10_000; i++)
        {
            (int row, int texelX) = RaylibPoseTexturePalette.GetBoneTexelAddress(i & 31, i % 615, rowsPerPose: 3);
            checksum += row + texelX;
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10_000; i++)
        {
            (int row, int texelX) = RaylibPoseTexturePalette.GetBoneTexelAddress(i & 31, i % 615, rowsPerPose: 3);
            checksum += row + texelX;
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.That(allocated, Is.Zero);
        Assert.That(checksum, Is.GreaterThan(0));
    }

    [Test]
    public void MainAndShadowShaders_UseTheSameLinearTwoDimensionalAddressing()
    {
        string root = FindRepoRoot();
        string main = File.ReadAllText(Path.Combine(root, "src", "Platforms", "Desktop", "skinning_instanced_pose_texture.vs"));
        string shadow = File.ReadAllText(Path.Combine(root, "src", "Platforms", "Desktop", "shadow_depth_skinning_pose_texture.vs"));

        AssertLinearAddressing(main);
        AssertLinearAddressing(shadow);
    }

    private static void AssertLinearAddressing(string shader)
    {
        Assert.That(shader, Does.Contain("textureSize(uBonePalette, 0).x"));
        Assert.That(shader, Does.Contain("const int BONE_TEXELS_PER_SLOT = 4;"));
        Assert.That(shader, Does.Contain("poseTextureRow * paletteWidth + boneSlot * BONE_TEXELS_PER_SLOT"));
        Assert.That(shader, Does.Contain("linearTexel % paletteWidth"));
        Assert.That(shader, Does.Contain("linearTexel / paletteWidth"));
    }

    private static string FindRepoRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "mods")) &&
                File.Exists(Path.Combine(directory.FullName, "AGENTS.md")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Ludots repository root was not found.");
    }
}
