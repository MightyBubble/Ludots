using System;
using System.Collections.Generic;

namespace Ludots.UI.Runtime;

internal sealed class UiStyleBuilder
{
	public string? Id;
	public string? ClassName;
	public UiDisplay Display = UiDisplay.Flex;
	public UiFlexDirection FlexDirection = UiFlexDirection.Column;
	public UiJustifyContent JustifyContent = UiJustifyContent.Start;
	public UiAlignItems AlignItems = UiAlignItems.Stretch;
	public UiAlignContent AlignContent = UiAlignContent.Stretch;
	public UiFlexWrap FlexWrap = UiFlexWrap.NoWrap;
	public UiPositionType PositionType = UiPositionType.Relative;
	public UiOverflow Overflow = UiOverflow.Visible;
	public UiPointerEvents PointerEvents = UiPointerEvents.Auto;
	public UiLength Left = UiLength.Auto;
	public UiLength Top = UiLength.Auto;
	public UiLength Right = UiLength.Auto;
	public UiLength Bottom = UiLength.Auto;
	public UiLength Width = UiLength.Auto;
	public UiLength Height = UiLength.Auto;
	public UiLength MinWidth = UiLength.Auto;
	public UiLength MinHeight = UiLength.Auto;
	public UiLength MaxWidth = UiLength.Auto;
	public UiLength MaxHeight = UiLength.Auto;
	public UiLength FlexBasis = UiLength.Auto;
	public float FlexGrow;
	public float FlexShrink;
	public float Gap;
	public float RowGap;
	public float ColumnGap;
	public IReadOnlyList<UiGridTrack> GridTemplateColumns = Array.Empty<UiGridTrack>();
	public IReadOnlyList<UiGridTrack> GridTemplateRows = Array.Empty<UiGridTrack>();
	public UiGridAutoFlow GridAutoFlow = UiGridAutoFlow.Row;
	public UiGridPlacement GridColumn = UiGridPlacement.Auto;
	public UiGridPlacement GridRow = UiGridPlacement.Auto;
	public UiThickness Margin = UiThickness.Zero;
	public UiThickness Padding = UiThickness.Zero;
	public float BorderWidth;
	public float BorderRadius;
	public float OutlineWidth;
	public int ZIndex;
	public UiColor BackgroundColor = UiColor.Transparent;
	public UiLinearGradient? BackgroundGradient;
	public IReadOnlyList<UiBackgroundLayer> BackgroundLayers = Array.Empty<UiBackgroundLayer>();
	public IReadOnlyList<UiBackgroundSize> BackgroundSizes = Array.Empty<UiBackgroundSize>();
	public IReadOnlyList<UiBackgroundPosition> BackgroundPositions = Array.Empty<UiBackgroundPosition>();
	public IReadOnlyList<UiBackgroundRepeat> BackgroundRepeats = Array.Empty<UiBackgroundRepeat>();
	public UiColor BorderColor = UiColor.Transparent;
	public UiBorderStyle BorderStyle = UiBorderStyle.Solid;
	public UiColor OutlineColor = UiColor.Transparent;
	public UiShadow? BoxShadow;
	public IReadOnlyList<UiShadow> BoxShadows = Array.Empty<UiShadow>();
	public float FilterBlurRadius;
	public float BackdropBlurRadius;
	public UiColor Color = UiColor.White;
	public UiShadow? TextShadow;
	public float FontSize = 16f;
	public string? FontFamily;
	public bool Bold;
	public bool Italic;
	public UiTextDirection Direction = UiTextDirection.Ltr;
	public UiTextAlign TextAlign = UiTextAlign.Start;
	public UiTextDecorationLine TextDecorationLine = UiTextDecorationLine.None;
	public UiTextOverflow TextOverflow = UiTextOverflow.Clip;
	public UiWhiteSpace WhiteSpace = UiWhiteSpace.Normal;
	public UiObjectFit ObjectFit = UiObjectFit.Fill;
	public UiThickness ImageSlice = UiThickness.Zero;
	public UiTransform Transform = UiTransform.Identity;
	public UiClipPath? ClipPath;
	public UiLinearGradient? MaskGradient;
	public UiTransitionSpec? Transition;
	public UiAnimationSpec? Animation;
	public float Opacity = 1f;
	public bool Visible = true;
	public bool ClipContent;

	public void CopyFrom(UiStyle style)
	{
		ArgumentNullException.ThrowIfNull(style);
		Id = style.Id;
		ClassName = style.ClassName;
		Display = style.Display;
		FlexDirection = style.FlexDirection;
		JustifyContent = style.JustifyContent;
		AlignItems = style.AlignItems;
		AlignContent = style.AlignContent;
		FlexWrap = style.FlexWrap;
		PositionType = style.PositionType;
		Overflow = style.Overflow;
		PointerEvents = style.PointerEvents;
		Left = style.Left;
		Top = style.Top;
		Right = style.Right;
		Bottom = style.Bottom;
		Width = style.Width;
		Height = style.Height;
		MinWidth = style.MinWidth;
		MinHeight = style.MinHeight;
		MaxWidth = style.MaxWidth;
		MaxHeight = style.MaxHeight;
		FlexBasis = style.FlexBasis;
		FlexGrow = style.FlexGrow;
		FlexShrink = style.FlexShrink;
		Gap = style.Gap;
		RowGap = style.RowGap;
		ColumnGap = style.ColumnGap;
		GridTemplateColumns = style.GridTemplateColumns;
		GridTemplateRows = style.GridTemplateRows;
		GridAutoFlow = style.GridAutoFlow;
		GridColumn = style.GridColumn;
		GridRow = style.GridRow;
		Margin = style.Margin;
		Padding = style.Padding;
		BorderWidth = style.BorderWidth;
		BorderRadius = style.BorderRadius;
		OutlineWidth = style.OutlineWidth;
		ZIndex = style.ZIndex;
		BackgroundColor = style.BackgroundColor;
		BackgroundGradient = style.BackgroundGradient;
		BackgroundLayers = style.BackgroundLayers;
		BackgroundSizes = style.BackgroundSizes;
		BackgroundPositions = style.BackgroundPositions;
		BackgroundRepeats = style.BackgroundRepeats;
		BorderColor = style.BorderColor;
		BorderStyle = style.BorderStyle;
		OutlineColor = style.OutlineColor;
		BoxShadow = style.BoxShadow;
		BoxShadows = style.BoxShadows;
		FilterBlurRadius = style.FilterBlurRadius;
		BackdropBlurRadius = style.BackdropBlurRadius;
		Color = style.Color;
		TextShadow = style.TextShadow;
		FontSize = style.FontSize;
		FontFamily = style.FontFamily;
		Bold = style.Bold;
		Italic = style.Italic;
		Direction = style.Direction;
		TextAlign = style.TextAlign;
		TextDecorationLine = style.TextDecorationLine;
		TextOverflow = style.TextOverflow;
		WhiteSpace = style.WhiteSpace;
		ObjectFit = style.ObjectFit;
		ImageSlice = style.ImageSlice;
		Transform = style.Transform;
		ClipPath = style.ClipPath;
		MaskGradient = style.MaskGradient;
		Transition = style.Transition;
		Animation = style.Animation;
		Opacity = style.Opacity;
		Visible = style.Visible;
		ClipContent = style.ClipContent;
	}

	public UiStyle ToStyle()
	{
		return new UiStyle
		{
			Id = Id,
			ClassName = ClassName,
			Display = Display,
			FlexDirection = FlexDirection,
			JustifyContent = JustifyContent,
			AlignItems = AlignItems,
			AlignContent = AlignContent,
			FlexWrap = FlexWrap,
			PositionType = PositionType,
			Overflow = Overflow,
			PointerEvents = PointerEvents,
			Left = Left,
			Top = Top,
			Right = Right,
			Bottom = Bottom,
			Width = Width,
			Height = Height,
			MinWidth = MinWidth,
			MinHeight = MinHeight,
			MaxWidth = MaxWidth,
			MaxHeight = MaxHeight,
			FlexBasis = FlexBasis,
			FlexGrow = FlexGrow,
			FlexShrink = FlexShrink,
			Gap = Gap,
			RowGap = RowGap,
			ColumnGap = ColumnGap,
			GridTemplateColumns = GridTemplateColumns,
			GridTemplateRows = GridTemplateRows,
			GridAutoFlow = GridAutoFlow,
			GridColumn = GridColumn,
			GridRow = GridRow,
			Margin = Margin,
			Padding = Padding,
			BorderWidth = BorderWidth,
			BorderRadius = BorderRadius,
			OutlineWidth = OutlineWidth,
			ZIndex = ZIndex,
			BackgroundColor = BackgroundColor,
			BackgroundGradient = BackgroundGradient,
			BackgroundLayers = BackgroundLayers,
			BackgroundSizes = BackgroundSizes,
			BackgroundPositions = BackgroundPositions,
			BackgroundRepeats = BackgroundRepeats,
			BorderColor = BorderColor,
			BorderStyle = BorderStyle,
			OutlineColor = OutlineColor,
			BoxShadow = BoxShadow,
			BoxShadows = BoxShadows,
			FilterBlurRadius = FilterBlurRadius,
			BackdropBlurRadius = BackdropBlurRadius,
			Color = Color,
			TextShadow = TextShadow,
			FontSize = FontSize,
			FontFamily = FontFamily,
			Bold = Bold,
			Italic = Italic,
			Direction = Direction,
			TextAlign = TextAlign,
			TextDecorationLine = TextDecorationLine,
			TextOverflow = TextOverflow,
			WhiteSpace = WhiteSpace,
			ObjectFit = ObjectFit,
			ImageSlice = ImageSlice,
			Transform = Transform,
			ClipPath = ClipPath,
			MaskGradient = MaskGradient,
			Transition = Transition,
			Animation = Animation,
			Opacity = Opacity,
			Visible = Visible,
			ClipContent = ClipContent,
		};
	}
}
