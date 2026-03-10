using SkiaSharp;

namespace Ludots.UI.Runtime;

internal static class UiTransformMath
{
	public static SKMatrix CreateMatrix(UiStyle style, UiRect rect)
	{
		SKMatrix sKMatrix = SKMatrix.Identity;
		if (style.Transform == null || !style.Transform.HasOperations)
		{
			return sKMatrix;
		}
		float pivotX = rect.X + rect.Width * 0.5f;
		float pivotY = rect.Y + rect.Height * 0.5f;
		for (int i = 0; i < style.Transform.Operations.Count; i++)
		{
			UiTransformOperation uiTransformOperation = style.Transform.Operations[i];
			UiTransformOperationKind kind = uiTransformOperation.Kind;
			if (1 == 0)
			{
			}
			SKMatrix sKMatrix2 = kind switch
			{
				UiTransformOperationKind.Translate => SKMatrix.CreateTranslation(ResolveLength(uiTransformOperation.XLength, rect.Width), ResolveLength(uiTransformOperation.YLength, rect.Height)), 
				UiTransformOperationKind.Scale => SKMatrix.CreateScale(uiTransformOperation.ScaleX, uiTransformOperation.ScaleY, pivotX, pivotY), 
				UiTransformOperationKind.Rotate => SKMatrix.CreateRotationDegrees(uiTransformOperation.AngleDegrees, pivotX, pivotY), 
				_ => SKMatrix.Identity, 
			};
			if (1 == 0)
			{
			}
			SKMatrix second = sKMatrix2;
			sKMatrix = SKMatrix.Concat(sKMatrix, second);
		}
		return sKMatrix;
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
