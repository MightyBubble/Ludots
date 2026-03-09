using SkiaSharp;

namespace Ludots.UI.Runtime;

internal static class UiTransformMath
{
    public static SKMatrix CreateMatrix(UiStyle style, UiRect rect)
    {
        SKMatrix matrix = SKMatrix.Identity;
        if (style.Transform == null || !style.Transform.HasOperations)
        {
            return matrix;
        }

        float centerX = rect.X + (rect.Width * 0.5f);
        float centerY = rect.Y + (rect.Height * 0.5f);
        for (int i = 0; i < style.Transform.Operations.Count; i++)
        {
            UiTransformOperation operation = style.Transform.Operations[i];
            SKMatrix operationMatrix = operation.Kind switch
            {
                UiTransformOperationKind.Translate => SKMatrix.CreateTranslation(ResolveLength(operation.XLength, rect.Width), ResolveLength(operation.YLength, rect.Height)),
                UiTransformOperationKind.Scale => SKMatrix.CreateScale(operation.ScaleX, operation.ScaleY, centerX, centerY),
                UiTransformOperationKind.Rotate => SKMatrix.CreateRotationDegrees(operation.AngleDegrees, centerX, centerY),
                _ => SKMatrix.Identity
            };

            matrix = SKMatrix.Concat(matrix, operationMatrix);
        }

        return matrix;
    }

    public static bool TryInvert(SKMatrix matrix, out SKMatrix inverse)
    {
        return matrix.TryInvert(out inverse);
    }

    private static float ResolveLength(UiLength length, float available)
    {
        return length.IsAuto ? 0f : length.Resolve(available);
    }
}
