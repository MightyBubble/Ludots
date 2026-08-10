using System;

namespace Ludots.UI.Runtime;

public enum UiCalcOperator : byte
{
	Add,
	Subtract,
	Multiply,
	Divide
}

/// <summary>
/// Immutable binary expression tree for CSS calc(). Leaves hold a <see cref="UiLength"/>
/// term (px/%/vw/vh/vmin/vmax). Unitless numbers in the source are stored as px-valued
/// terms: Evaluate only uses the raw numeric value, so multiplication/division remain
/// numerically correct under that simplification.
/// </summary>
public sealed class UiCalcExpression
{
	public UiCalcExpression? Left { get; }

	public UiCalcOperator Operator { get; }

	public UiCalcExpression? Right { get; }

	public UiLength? Term { get; }

	public bool IsLeaf => Left == null && Right == null;

	public UiCalcExpression(UiLength term)
	{
		Term = term;
	}

	public UiCalcExpression(UiCalcExpression left, UiCalcOperator op, UiCalcExpression right)
	{
		Left = left;
		Operator = op;
		Right = right;
	}

	/// <summary>True when any leaf is a percent term; percent needs the containing-block size at evaluation.</summary>
	public bool HasPercentTerm => HasUnitTerm(UiLengthUnit.Percent);

	private bool HasUnitTerm(UiLengthUnit unit)
	{
		if (IsLeaf)
		{
			return Term?.Unit == unit;
		}
		return (Left != null && Left.HasUnitTerm(unit)) || (Right != null && Right.HasUnitTerm(unit));
	}

	public float Evaluate(UiLengthContext context)
	{
		if (IsLeaf)
		{
			UiLength? term = Term;
			return term.HasValue ? term.Value.Resolve(context) : float.NaN;
		}
		float left = Left?.Evaluate(context) ?? float.NaN;
		float right = Right?.Evaluate(context) ?? float.NaN;
		if (float.IsNaN(left) || float.IsNaN(right))
		{
			return float.NaN;
		}
		return Operator switch
		{
			UiCalcOperator.Add => left + right,
			UiCalcOperator.Subtract => left - right,
			UiCalcOperator.Multiply => left * right,
			UiCalcOperator.Divide => Math.Abs(right) < 1e-6f ? float.NaN : left / right,
			_ => float.NaN
		};
	}
}
