using Ludots.Raylib.Render;
using NUnit.Framework;
using Raylib_cs;
using Rl = Raylib_cs.Raylib;

namespace Ludots.Tests.RaylibAdapter;

[TestFixture]
[NonParallelizable]
[Category("NativeGraphics")]
public sealed class RaylibPoseTexturePaletteTests
{
    [Test]
    public void SoldierPalette_Accepts615SlotsAndKeepsUploadsInsideTexture()
    {
        const uint HiddenWindow = 0x80;
        Rl.SetConfigFlags(HiddenWindow);
        Rl.InitWindow(64, 64, "Pose palette regression");
        try
        {
            using var palette = new RaylibPoseTexturePalette(initialPoseRows: 16);
            palette.EnsureBoneSlotCapacity(615);
            palette.EnsurePoseRowCapacity(65);
            var matrix = new RaylibMatrix { m0 = 1, m5 = 1, m10 = 1, m15 = 1 };
            for (int pose = 0; pose < 65; pose++)
            {
                for (int bone = 0; bone < 615; bone++)
                {
                    palette.WriteBoneMatrix(pose, bone, in matrix);
                }
                palette.FlushPaletteRow(pose);
            }

            Assert.That(palette.BonePalette.id, Is.Not.Zero);
            Assert.That(palette.BonePalette.width, Is.LessThanOrEqualTo(2048));
            Assert.That(palette.BonePalette.height, Is.LessThanOrEqualTo(2048));
            Assert.Throws<InvalidOperationException>(() =>
                palette.EnsureBoneSlotCapacity(RaylibPoseTexturePalette.MaxBoneSlotCapacity + 1));
        }
        finally
        {
            Rl.CloseWindow();
        }
    }
}
