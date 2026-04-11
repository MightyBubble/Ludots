using System;

namespace MassNavWebParityMod.Runtime;

public sealed class MassNavFormationRuntime
{
    public void BuildOffsets(
        float[] baseOffsetX,
        float[] baseOffsetY,
        float[] offsetX,
        float[] offsetY,
        int count,
        MassNavFormationMode mode,
        float rotationRadians)
    {
        const float lineSpacingCm = 180f;
        const float squareSpacingCm = 80f;
        const float wedgeSpacingCm = 180f;

        switch (mode)
        {
            case MassNavFormationMode.Line:
                for (int i = 0; i < count; i++)
                {
                    baseOffsetX[i] = (i - ((count - 1) * 0.5f)) * lineSpacingCm;
                    baseOffsetY[i] = 0f;
                    offsetX[i] = baseOffsetX[i];
                    offsetY[i] = 0f;
                }
                break;

            case MassNavFormationMode.Circle:
                float radius = MathF.Max(200f, count * lineSpacingCm / (MathF.PI * 2f));
                for (int i = 0; i < count; i++)
                {
                    float angle = (i / (float)count) * MathF.PI * 2f;
                    baseOffsetX[i] = MathF.Cos(angle) * radius;
                    baseOffsetY[i] = MathF.Sin(angle) * radius;
                    offsetX[i] = baseOffsetX[i];
                    offsetY[i] = baseOffsetY[i];
                }
                break;

            case MassNavFormationMode.Wedge:
                for (int i = 0; i < count; i++)
                {
                    if (i == 0)
                    {
                        baseOffsetX[i] = 0f;
                        baseOffsetY[i] = 0f;
                    }
                    else
                    {
                        int row = (int)Math.Ceiling(i / 2f);
                        int side = (i & 1) == 1 ? 1 : -1;
                        baseOffsetX[i] = side * row * wedgeSpacingCm;
                        baseOffsetY[i] = row * wedgeSpacingCm;
                    }

                    offsetX[i] = baseOffsetX[i];
                    offsetY[i] = baseOffsetY[i];
                }
                break;

            case MassNavFormationMode.Square:
            default:
                int cols = (int)Math.Ceiling(Math.Sqrt(count));
                int rows = (int)Math.Ceiling(count / (double)cols);
                float rowCenter = (rows - 1) * 0.5f;
                float colCenter = (cols - 1) * 0.5f;
                for (int i = 0; i < count; i++)
                {
                    int row = i / cols;
                    int col = i % cols;
                    baseOffsetX[i] = (col - colCenter) * squareSpacingCm;
                    baseOffsetY[i] = (row - rowCenter) * squareSpacingCm;
                    offsetX[i] = baseOffsetX[i];
                    offsetY[i] = baseOffsetY[i];
                }
                break;
        }

        if (MathF.Abs(rotationRadians) > 1e-5f)
        {
            ApplyRotation(offsetX, offsetY, baseOffsetX, baseOffsetY, rotationRadians);
        }
    }

    public void RecomputeOffsets(float[] offsetX, float[] offsetY, float[] baseOffsetX, float[] baseOffsetY, float rotationRadians)
    {
        ApplyRotation(offsetX, offsetY, baseOffsetX, baseOffsetY, rotationRadians);
    }

    private static void ApplyRotation(float[] offsetX, float[] offsetY, float[] baseOffsetX, float[] baseOffsetY, float rotationRadians)
    {
        float cos = MathF.Cos(rotationRadians);
        float sin = MathF.Sin(rotationRadians);
        for (int i = 0; i < baseOffsetX.Length; i++)
        {
            float x = baseOffsetX[i];
            float y = baseOffsetY[i];
            offsetX[i] = (x * cos) - (y * sin);
            offsetY[i] = (x * sin) + (y * cos);
        }
    }
}
