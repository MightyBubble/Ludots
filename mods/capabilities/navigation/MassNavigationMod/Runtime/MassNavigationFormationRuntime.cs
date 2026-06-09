using System;

namespace MassNavigationMod.Runtime;

public sealed class MassNavigationFormationRuntime
{
    private readonly MassNavigationGroupSemantics _groupSemantics;

    public MassNavigationFormationRuntime(MassNavigationGroupSemantics groupSemantics)
    {
        _groupSemantics = groupSemantics ?? throw new ArgumentNullException(nameof(groupSemantics));
    }

    public float RotationEpsilonRadians => _groupSemantics.FormationRotationEpsilonRadians;

    public void BuildOffsets(
        float[] baseOffsetX,
        float[] baseOffsetY,
        float[] offsetX,
        float[] offsetY,
        int count,
        MassNavigationFormationMode mode,
        float rotationRadians)
    {
        switch (mode)
        {
            case MassNavigationFormationMode.Line:
                for (int i = 0; i < count; i++)
                {
                    baseOffsetX[i] = (i - ((count - 1) * 0.5f)) * _groupSemantics.FormationLineSpacingCm;
                    baseOffsetY[i] = 0f;
                    offsetX[i] = baseOffsetX[i];
                    offsetY[i] = 0f;
                }
                break;

            case MassNavigationFormationMode.Circle:
                float radius = MathF.Max(
                    _groupSemantics.FormationCircleMinRadiusCm,
                    count * _groupSemantics.FormationCircleSpacingCm / (MathF.PI * 2f));
                for (int i = 0; i < count; i++)
                {
                    float angle = (i / (float)count) * MathF.PI * 2f;
                    baseOffsetX[i] = MathF.Cos(angle) * radius;
                    baseOffsetY[i] = MathF.Sin(angle) * radius;
                    offsetX[i] = baseOffsetX[i];
                    offsetY[i] = baseOffsetY[i];
                }
                break;

            case MassNavigationFormationMode.Wedge:
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
                        baseOffsetX[i] = side * row * _groupSemantics.FormationWedgeSpacingCm;
                        baseOffsetY[i] = row * _groupSemantics.FormationWedgeSpacingCm;
                    }

                    offsetX[i] = baseOffsetX[i];
                    offsetY[i] = baseOffsetY[i];
                }
                break;

            case MassNavigationFormationMode.Square:
                int cols = (int)Math.Ceiling(Math.Sqrt(count));
                int rows = (int)Math.Ceiling(count / (double)cols);
                float rowCenter = (rows - 1) * 0.5f;
                float colCenter = (cols - 1) * 0.5f;
                for (int i = 0; i < count; i++)
                {
                    int row = i / cols;
                    int col = i % cols;
                    baseOffsetX[i] = (col - colCenter) * _groupSemantics.FormationSquareSpacingCm;
                    baseOffsetY[i] = (row - rowCenter) * _groupSemantics.FormationSquareSpacingCm;
                    offsetX[i] = baseOffsetX[i];
                    offsetY[i] = baseOffsetY[i];
                }
                break;

            default:
                throw new InvalidOperationException($"MassNavigation formation mode '{mode}' is not supported.");
        }

        if (MathF.Abs(rotationRadians) > _groupSemantics.FormationRotationEpsilonRadians)
        {
            ApplyRotation(offsetX, offsetY, baseOffsetX, baseOffsetY, count, rotationRadians);
        }
    }

    public void RecomputeOffsets(
        float[] offsetX,
        float[] offsetY,
        float[] baseOffsetX,
        float[] baseOffsetY,
        int count,
        float rotationRadians)
    {
        ApplyRotation(offsetX, offsetY, baseOffsetX, baseOffsetY, count, rotationRadians);
    }

    private static void ApplyRotation(
        float[] offsetX,
        float[] offsetY,
        float[] baseOffsetX,
        float[] baseOffsetY,
        int count,
        float rotationRadians)
    {
        float cos = MathF.Cos(rotationRadians);
        float sin = MathF.Sin(rotationRadians);
        for (int i = 0; i < count; i++)
        {
            float x = baseOffsetX[i];
            float y = baseOffsetY[i];
            offsetX[i] = (x * cos) - (y * sin);
            offsetY[i] = (x * sin) + (y * cos);
        }
    }
}

