namespace FlexLayoutSharp;

internal class Style
{
	internal Direction Direction = Direction.Inherit;

	internal FlexDirection FlexDirection = FlexDirection.Column;

	internal Justify JustifyContent = Justify.FlexStart;

	internal Align AlignContent = Align.FlexStart;

	internal Align AlignItems = Align.Stretch;

	internal Align AlignSelf;

	internal PositionType PositionType;

	internal Wrap FlexWrap;

	internal Overflow Overflow = Overflow.Visible;

	internal Display Display = Display.Flex;

	internal float Flex = float.NaN;

	internal float FlexGrow = float.NaN;

	internal float FlexShrink = float.NaN;

	internal Value FlexBasis = CreateAutoValue();

	internal readonly Value[] Margin = CreateDefaultEdgeValuesUnit();

	internal readonly Value[] Position = CreateDefaultEdgeValuesUnit();

	internal readonly Value[] Padding = CreateDefaultEdgeValuesUnit();

	internal readonly Value[] Border = CreateDefaultEdgeValuesUnit();

	internal readonly Value[] Dimensions = new Value[2]
	{
		CreateAutoValue(),
		CreateAutoValue()
	};

	internal readonly Value[] MinDimensions = new Value[2]
	{
		Value.UndefinedValue,
		Value.UndefinedValue
	};

	internal readonly Value[] MaxDimensions = new Value[2]
	{
		Value.UndefinedValue,
		Value.UndefinedValue
	};

	internal float AspectRatio = float.NaN;

	internal static Value CreateAutoValue()
	{
		return new Value(float.NaN, Unit.Auto);
	}

	internal static Value[] CreateDefaultEdgeValuesUnit()
	{
		return new Value[9]
		{
			Value.UndefinedValue,
			Value.UndefinedValue,
			Value.UndefinedValue,
			Value.UndefinedValue,
			Value.UndefinedValue,
			Value.UndefinedValue,
			Value.UndefinedValue,
			Value.UndefinedValue,
			Value.UndefinedValue
		};
	}

	internal static void Copy(Style dest, Style src)
	{
		dest.Direction = src.Direction;
		dest.FlexDirection = src.FlexDirection;
		dest.JustifyContent = src.JustifyContent;
		dest.AlignContent = src.AlignContent;
		dest.AlignItems = src.AlignItems;
		dest.AlignSelf = src.AlignSelf;
		dest.PositionType = src.PositionType;
		dest.FlexWrap = src.FlexWrap;
		dest.Overflow = src.Overflow;
		dest.Display = src.Display;
		dest.Flex = src.Flex;
		dest.FlexGrow = src.FlexGrow;
		dest.FlexShrink = src.FlexShrink;
		Value.CopyValue(dest.Margin, src.Margin);
		Value.CopyValue(dest.Position, src.Position);
		Value.CopyValue(dest.Padding, src.Padding);
		Value.CopyValue(dest.Border, src.Border);
		Value.CopyValue(dest.Dimensions, src.Dimensions);
		Value.CopyValue(dest.MinDimensions, src.MinDimensions);
		Value.CopyValue(dest.MaxDimensions, src.MaxDimensions);
		dest.AspectRatio = src.AspectRatio;
	}
}
