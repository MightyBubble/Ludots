using System;
using System.Numerics;

namespace Ludots.UI.Runtime;

internal static class UiTransformMath
{
	public static Matrix3x2 CreateMatrix(UiStyle style, UiRect rect)
	{
		Matrix3x2 result = Matrix3x2.Identity;
		if (style.Transform == null || !style.Transform.HasOperations)
		{
			return result;
		}
		float pivotX = rect.X + rect.Width * 0.5f;
		float pivotY = rect.Y + rect.Height * 0.5f;
		for (int i = 0; i < style.Transform.Operations.Count; i++)
		{
			UiTransformOperation op = style.Transform.Operations[i];
			Matrix3x2 opMatrix = op.Kind switch
			{
				UiTransformOperationKind.Translate => Matrix3x2.CreateTranslation(
					ResolveLength(op.XLength, rect.Width),
					ResolveLength(op.YLength, rect.Height)),
				UiTransformOperationKind.Scale => CreateScaleAroundPivot(
					op.ScaleX, op.ScaleY, pivotX, pivotY),
				UiTransformOperationKind.Rotate => CreateRotationAroundPivot(
					op.AngleDegrees * (MathF.PI / 180f), pivotX, pivotY),
				_ => Matrix3x2.Identity,
			};
			result = result * opMatrix;
		}
		return result;
	}

	private static Matrix3x2 CreateScaleAroundPivot(float sx, float sy, float px, float py)
	{
		return Matrix3x2.CreateTranslation(-px, -py) *
		       Matrix3x2.CreateScale(sx, sy) *
		       Matrix3x2.CreateTranslation(px, py);
	}

	private static Matrix3x2 CreateRotationAroundPivot(float radians, float px, float py)
	{
		return Matrix3x2.CreateTranslation(-px, -py) *
		       Matrix3x2.CreateRotation(radians) *
		       Matrix3x2.CreateTranslation(px, py);
	}

	private static float ResolveLength(UiLength length, float available)
	{
		return length.IsAuto ? 0f : length.Resolve(available);
	}
}
