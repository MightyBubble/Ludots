using System.Numerics;
using System.Runtime.InteropServices;
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
    public void RaylibMatrix_FieldOffsetsMatchRaylibNativeLayout()
    {
        string[] fields =
        [
            nameof(RaylibMatrix.m0), nameof(RaylibMatrix.m4), nameof(RaylibMatrix.m8), nameof(RaylibMatrix.m12),
            nameof(RaylibMatrix.m1), nameof(RaylibMatrix.m5), nameof(RaylibMatrix.m9), nameof(RaylibMatrix.m13),
            nameof(RaylibMatrix.m2), nameof(RaylibMatrix.m6), nameof(RaylibMatrix.m10), nameof(RaylibMatrix.m14),
            nameof(RaylibMatrix.m3), nameof(RaylibMatrix.m7), nameof(RaylibMatrix.m11), nameof(RaylibMatrix.m15),
        ];

        Assert.That(Marshal.SizeOf<RaylibMatrix>(), Is.EqualTo(16 * sizeof(float)));
        for (int i = 0; i < fields.Length; i++)
        {
            Assert.That(Marshal.OffsetOf<RaylibMatrix>(fields[i]).ToInt32(), Is.EqualTo(i * sizeof(float)), fields[i]);
        }
    }

    [Test]
    public void AffineBonePacking_PreservesMat4PositionAndMat3NormalSemanticsWithNonUniformScale()
    {
        Matrix4x4 transform =
            Matrix4x4.CreateScale(1.5f, 0.625f, 2.25f) *
            Matrix4x4.CreateFromYawPitchRoll(0.73f, -0.41f, 1.17f) *
            Matrix4x4.CreateTranslation(17.5f, -8.25f, 31.75f);
        RaylibMatrix matrix = RaylibMatrix.FromSystemNumerics(in transform);
        Span<float> packed = stackalloc float[RaylibPoseTexturePalette.TexelsPerBoneSlot * 4];

        RaylibPoseTexturePalette.PackAffineBoneMatrix(in matrix, packed);

        Vector3 position = new(3.25f, -7.5f, 2.125f);
        Vector3 normal = Vector3.Normalize(new Vector3(-0.3f, 0.8f, 0.5f));
        Vector4 expectedPosition = TransformPosition(in matrix, position);
        Vector4 packedPosition = TransformPackedPosition(packed, position);
        Vector3 expectedNormal = TransformNormal(in matrix, normal);
        Vector3 packedNormal = TransformPackedNormal(packed, normal);

        AssertVector(expectedPosition, packedPosition);
        AssertVector(expectedNormal, packedNormal);
    }

    [Test]
    public void AffineBonePacking_RejectsNonAffineMatrix()
    {
        var matrix = new RaylibMatrix { m0 = 1f, m5 = 1f, m10 = 1f, m15 = 1f, m3 = 0.25f };
        var packed = new float[RaylibPoseTexturePalette.TexelsPerBoneSlot * 4];
        Assert.Throws<InvalidOperationException>(() =>
            RaylibPoseTexturePalette.PackAffineBoneMatrix(in matrix, packed));
    }

    [Test]
    public void InstancePacking_RoundTripsRgb24AndQuantizedAlpha()
    {
        Span<float> packed = stackalloc float[4];
        RaylibPoseTexturePalette.PackInstance(777, 0.501f, 1.5f, -0.5f, 0.25f, packed);

        int packedRgb = checked((int)packed[1]);
        Assert.That(packed[0], Is.EqualTo(777f));
        Assert.That(packedRgb & 0xff, Is.EqualTo(128));
        Assert.That((packedRgb >> 8) & 0xff, Is.EqualTo(255));
        Assert.That((packedRgb >> 16) & 0xff, Is.Zero);
        Assert.That(packed[2], Is.EqualTo(64f / 255f));
        Assert.That(packed[3], Is.Zero);

        RaylibPoseTexturePalette.PackInstance(0, 1f, 1f, 1f, 1f, packed);
        Assert.That(packed[1], Is.EqualTo(16_777_215f));
    }

    [Test]
    public void TenThousandPackedWrites_AllocateZeroBytes()
    {
        var matrix = RaylibMatrix.FromScaleTranslation(11f, 13f, 17f, 2f, 3f, 5f);
        var bone = new float[RaylibPoseTexturePalette.TexelsPerBoneSlot * 4];
        var instance = new float[4];
        MeasurePackedWrites(in matrix, bone, instance, 1);

        long allocated = MeasurePackedWrites(in matrix, bone, instance, 10_000);

        Assert.That(allocated, Is.Zero);
    }

    [Test]
    public void SoldierPalette_AcceptsConfiguredSlotsAndKeepsUploadsInsideTexture()
    {
        const uint HiddenWindow = 0x80;
        Rl.SetConfigFlags(HiddenWindow);
        Rl.InitWindow(64, 64, "Pose palette regression");
        try
        {
            using var palette = new RaylibPoseTexturePalette(poseRows: 65, instances: 10000, boneSlots: 615);
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
            Assert.That(palette.BonePalette.width, Is.EqualTo(768));
            Assert.That(palette.BonePalette.width, Is.LessThanOrEqualTo(2048));
            Assert.That(palette.BonePalette.height, Is.LessThanOrEqualTo(2048));
            Assert.That(palette.InstanceTable.width, Is.EqualTo(1024));
            Assert.That(palette.InstanceTable.height, Is.EqualTo(10));
            Assert.Throws<InvalidOperationException>(() =>
                palette.EnsureBoneSlotCapacity(RaylibPoseTexturePalette.MaxBoneSlotCapacity + 1));
            Assert.Throws<InvalidOperationException>(() => palette.EnsurePoseRowCapacity(66));
            Assert.Throws<InvalidOperationException>(() => palette.EnsureInstanceCapacity(10001));
        }
        finally
        {
            Rl.CloseWindow();
        }
    }

    private static long MeasurePackedWrites(in RaylibMatrix matrix, float[] bone, float[] instance, int count)
    {
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < count; i++)
        {
            RaylibPoseTexturePalette.PackAffineBoneMatrix(in matrix, bone);
            RaylibPoseTexturePalette.PackInstance(i, i / 255f, 0.25f, 0.75f, 1f, instance);
        }
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    private static Vector4 TransformPosition(in RaylibMatrix matrix, Vector3 value)
    {
        return new Vector4(
            matrix.m0 * value.X + matrix.m4 * value.Y + matrix.m8 * value.Z + matrix.m12,
            matrix.m1 * value.X + matrix.m5 * value.Y + matrix.m9 * value.Z + matrix.m13,
            matrix.m2 * value.X + matrix.m6 * value.Y + matrix.m10 * value.Z + matrix.m14,
            matrix.m3 * value.X + matrix.m7 * value.Y + matrix.m11 * value.Z + matrix.m15);
    }

    private static Vector4 TransformPackedPosition(ReadOnlySpan<float> packed, Vector3 value)
    {
        return new Vector4(
            packed[0] * value.X + packed[4] * value.Y + packed[8] * value.Z + packed[3],
            packed[1] * value.X + packed[5] * value.Y + packed[9] * value.Z + packed[7],
            packed[2] * value.X + packed[6] * value.Y + packed[10] * value.Z + packed[11],
            1f);
    }

    private static Vector3 TransformNormal(in RaylibMatrix matrix, Vector3 value)
    {
        return new Vector3(
            matrix.m0 * value.X + matrix.m4 * value.Y + matrix.m8 * value.Z,
            matrix.m1 * value.X + matrix.m5 * value.Y + matrix.m9 * value.Z,
            matrix.m2 * value.X + matrix.m6 * value.Y + matrix.m10 * value.Z);
    }

    private static Vector3 TransformPackedNormal(ReadOnlySpan<float> packed, Vector3 value)
    {
        return new Vector3(
            packed[0] * value.X + packed[4] * value.Y + packed[8] * value.Z,
            packed[1] * value.X + packed[5] * value.Y + packed[9] * value.Z,
            packed[2] * value.X + packed[6] * value.Y + packed[10] * value.Z);
    }

    private static void AssertVector(Vector4 expected, Vector4 actual)
    {
        Assert.That(actual.X, Is.EqualTo(expected.X).Within(0.00001f));
        Assert.That(actual.Y, Is.EqualTo(expected.Y).Within(0.00001f));
        Assert.That(actual.Z, Is.EqualTo(expected.Z).Within(0.00001f));
        Assert.That(actual.W, Is.EqualTo(expected.W).Within(0.00001f));
    }

    private static void AssertVector(Vector3 expected, Vector3 actual)
    {
        Assert.That(actual.X, Is.EqualTo(expected.X).Within(0.00001f));
        Assert.That(actual.Y, Is.EqualTo(expected.Y).Within(0.00001f));
        Assert.That(actual.Z, Is.EqualTo(expected.Z).Within(0.00001f));
    }
}
