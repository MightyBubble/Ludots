using SkiaSharp;

namespace Ludots.UI.Runtime;

public sealed record UiStyle
{
    public static UiStyle Default { get; } = new();

    public string? Id { get; init; }

    public string? ClassName { get; init; }

    public UiDisplay Display { get; init; } = UiDisplay.Flex;

    public UiFlexDirection FlexDirection { get; init; } = UiFlexDirection.Column;

    public UiJustifyContent JustifyContent { get; init; } = UiJustifyContent.Start;

    public UiAlignItems AlignItems { get; init; } = UiAlignItems.Stretch;

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

    public UiThickness Margin { get; init; } = UiThickness.Zero;

    public UiThickness Padding { get; init; } = UiThickness.Zero;

    public float BorderWidth { get; init; }

    public float BorderRadius { get; init; }

    public float OutlineWidth { get; init; }

    public SKColor BackgroundColor { get; init; } = SKColors.Transparent;

    public UiLinearGradient? BackgroundGradient { get; init; }

    public SKColor BorderColor { get; init; } = SKColors.Transparent;

    public SKColor OutlineColor { get; init; } = SKColors.Transparent;

    public UiShadow? BoxShadow { get; init; }

    public SKColor Color { get; init; } = SKColors.White;

    public UiShadow? TextShadow { get; init; }

    public float FontSize { get; init; } = 16f;

    public string? FontFamily { get; init; }

    public bool Bold { get; init; }

    public UiWhiteSpace WhiteSpace { get; init; } = UiWhiteSpace.Normal;

    public float Opacity { get; init; } = 1f;

    public bool Visible { get; init; } = true;

    public bool ClipContent { get; init; }
}
