using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using SkiaSharp;

namespace Ludots.UI.Runtime;

public sealed record UiStyle
{
	public static UiStyle Default { get; } = new UiStyle();

	public string? Id { get; init; }

	public string? ClassName { get; init; }

	public UiDisplay Display { get; init; } = UiDisplay.Flex;

	public UiFlexDirection FlexDirection { get; init; } = UiFlexDirection.Column;

	public UiJustifyContent JustifyContent { get; init; } = UiJustifyContent.Start;

	public UiAlignItems AlignItems { get; init; } = UiAlignItems.Stretch;

	public UiAlignContent AlignContent { get; init; } = UiAlignContent.Stretch;

	public UiFlexWrap FlexWrap { get; init; } = UiFlexWrap.NoWrap;

	public UiPositionType PositionType { get; init; } = UiPositionType.Relative;

	public UiOverflow Overflow { get; init; } = UiOverflow.Visible;

	public UiLength Left { get; init; } = UiLength.Auto;

	public UiLength Top { get; init; } = UiLength.Auto;

	public UiLength Right { get; init; } = UiLength.Auto;

	public UiLength Bottom { get; init; } = UiLength.Auto;

	public UiLength Width { get; init; } = UiLength.Auto;

	public UiLength Height { get; init; } = UiLength.Auto;

	public UiLength MinWidth { get; init; } = UiLength.Auto;

	public UiLength MinHeight { get; init; } = UiLength.Auto;

	public UiLength MaxWidth { get; init; } = UiLength.Auto;

	public UiLength MaxHeight { get; init; } = UiLength.Auto;

	public UiLength FlexBasis { get; init; } = UiLength.Auto;

	public float FlexGrow { get; init; }

	public float FlexShrink { get; init; }

	public float Gap { get; init; }

	public float RowGap { get; init; }

	public float ColumnGap { get; init; }

	public UiThickness Margin { get; init; } = UiThickness.Zero;

	public UiThickness Padding { get; init; } = UiThickness.Zero;

	public float BorderWidth { get; init; }

	public float BorderRadius { get; init; }

	public float OutlineWidth { get; init; }

	public int ZIndex { get; init; }

	public SKColor BackgroundColor { get; init; } = SKColors.Transparent;

	public UiLinearGradient? BackgroundGradient { get; init; }

	public IReadOnlyList<UiBackgroundLayer> BackgroundLayers { get; init; } = Array.Empty<UiBackgroundLayer>();

	public IReadOnlyList<UiBackgroundSize> BackgroundSizes { get; init; } = Array.Empty<UiBackgroundSize>();

	public IReadOnlyList<UiBackgroundPosition> BackgroundPositions { get; init; } = Array.Empty<UiBackgroundPosition>();

	public IReadOnlyList<UiBackgroundRepeat> BackgroundRepeats { get; init; } = Array.Empty<UiBackgroundRepeat>();

	public SKColor BorderColor { get; init; } = SKColors.Transparent;

	public UiBorderStyle BorderStyle { get; init; } = UiBorderStyle.Solid;

	public SKColor OutlineColor { get; init; } = SKColors.Transparent;

	public UiShadow? BoxShadow { get; init; }

	public IReadOnlyList<UiShadow> BoxShadows { get; init; } = Array.Empty<UiShadow>();

	public float FilterBlurRadius { get; init; }

	public float BackdropBlurRadius { get; init; }

	public SKColor Color { get; init; } = SKColors.White;

	public UiShadow? TextShadow { get; init; }

	public float FontSize { get; init; } = 16f;

	public string? FontFamily { get; init; }

	public bool Bold { get; init; }

	public UiTextDirection Direction { get; init; } = UiTextDirection.Ltr;

	public UiTextAlign TextAlign { get; init; } = UiTextAlign.Start;

	public UiTextDecorationLine TextDecorationLine { get; init; } = UiTextDecorationLine.None;

	public UiTextOverflow TextOverflow { get; init; } = UiTextOverflow.Clip;

	public UiWhiteSpace WhiteSpace { get; init; } = UiWhiteSpace.Normal;

	public UiObjectFit ObjectFit { get; init; } = UiObjectFit.Fill;

	public UiThickness ImageSlice { get; init; } = UiThickness.Zero;

	public UiTransform Transform { get; init; } = UiTransform.Identity;

	public UiClipPath? ClipPath { get; init; }

	public UiLinearGradient? MaskGradient { get; init; }

	public UiTransitionSpec? Transition { get; init; }

	public UiAnimationSpec? Animation { get; init; }

	public float Opacity { get; init; } = 1f;

	public bool Visible { get; init; } = true;

	public bool ClipContent { get; set; }

	[CompilerGenerated]
	private UiStyle(UiStyle original)
	{
		Id = original.Id;
		ClassName = original.ClassName;
		Display = original.Display;
		FlexDirection = original.FlexDirection;
		JustifyContent = original.JustifyContent;
		AlignItems = original.AlignItems;
		AlignContent = original.AlignContent;
		FlexWrap = original.FlexWrap;
		PositionType = original.PositionType;
		Overflow = original.Overflow;
		Left = original.Left;
		Top = original.Top;
		Right = original.Right;
		Bottom = original.Bottom;
		Width = original.Width;
		Height = original.Height;
		MinWidth = original.MinWidth;
		MinHeight = original.MinHeight;
		MaxWidth = original.MaxWidth;
		MaxHeight = original.MaxHeight;
		FlexBasis = original.FlexBasis;
		FlexGrow = original.FlexGrow;
		FlexShrink = original.FlexShrink;
		Gap = original.Gap;
		RowGap = original.RowGap;
		ColumnGap = original.ColumnGap;
		Margin = original.Margin;
		Padding = original.Padding;
		BorderWidth = original.BorderWidth;
		BorderRadius = original.BorderRadius;
		OutlineWidth = original.OutlineWidth;
		ZIndex = original.ZIndex;
		BackgroundColor = original.BackgroundColor;
		BackgroundGradient = original.BackgroundGradient;
		BackgroundLayers = original.BackgroundLayers;
		BackgroundSizes = original.BackgroundSizes;
		BackgroundPositions = original.BackgroundPositions;
		BackgroundRepeats = original.BackgroundRepeats;
		BorderColor = original.BorderColor;
		BorderStyle = original.BorderStyle;
		OutlineColor = original.OutlineColor;
		BoxShadow = original.BoxShadow;
		BoxShadows = original.BoxShadows;
		FilterBlurRadius = original.FilterBlurRadius;
		BackdropBlurRadius = original.BackdropBlurRadius;
		Color = original.Color;
		TextShadow = original.TextShadow;
		FontSize = original.FontSize;
		FontFamily = original.FontFamily;
		Bold = original.Bold;
		Direction = original.Direction;
		TextAlign = original.TextAlign;
		TextDecorationLine = original.TextDecorationLine;
		TextOverflow = original.TextOverflow;
		WhiteSpace = original.WhiteSpace;
		ObjectFit = original.ObjectFit;
		ImageSlice = original.ImageSlice;
		Transform = original.Transform;
		ClipPath = original.ClipPath;
		MaskGradient = original.MaskGradient;
		Transition = original.Transition;
		Animation = original.Animation;
		Opacity = original.Opacity;
		Visible = original.Visible;
		ClipContent = original.ClipContent;
	}

	public UiStyle()
	{
	}
}
