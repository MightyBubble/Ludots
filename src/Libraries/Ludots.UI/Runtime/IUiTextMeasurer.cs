using System;
using System.Collections.Generic;

namespace Ludots.UI.Runtime;

/// <summary>
/// Platform-agnostic text measurement strategy.
/// Injected into <see cref="UiLayoutEngine"/> so the layout layer
/// never touches a concrete rendering backend.
/// </summary>
public interface IUiTextMeasurer
{
    UiTextLayoutResult Measure(string? text, UiStyle style, float availableWidth, bool constrainWidth);

    float MeasureWidth(string? text, UiStyle style);

    /// <summary>
    /// 排版只要文字盒的宽高。和 <see cref="Measure(string?, UiStyle, float, bool)"/> 折行规则一致，
    /// 但不产出行字符串，排版热路径上不分配。
    /// </summary>
    UiTextSize MeasureSize(ReadOnlySpan<char> text, UiStyle style, float availableWidth, bool constrainWidth);

    UiTextLayoutResult Measure(IReadOnlyList<UiStyledTextRun> runs, UiStyle style, float availableWidth, bool constrainWidth)
        => Measure(UiStyledTextRunNormalization.ConcatPlain(runs), style, availableWidth, constrainWidth);

    float MeasureWidth(IReadOnlyList<UiStyledTextRun> runs, UiStyle style)
        => MeasureWidth(UiStyledTextRunNormalization.ConcatPlain(runs), style);
}

public readonly record struct UiTextSize(float Width, float Height);
