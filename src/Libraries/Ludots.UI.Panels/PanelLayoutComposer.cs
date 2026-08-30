using System;
using System.Collections.Generic;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.UI.PanelProjection;
using Ludots.UI.Compose;
using Ludots.UI.Runtime;

namespace Ludots.UI.Panels;

public interface IPanelLayoutBindingScope
{
    string ReadText(string bind);
    float ReadFloat(string bind);
    bool ReadBool(string bind);
    IReadOnlyList<PresentationTextRun> ReadTextRuns(string bind);
    IReadOnlyList<IPanelLayoutBindingScope> ReadList(string bind);
    bool IsPresent(string bind);
}

public delegate string PanelLayoutImageSourceResolver(string imageReference);

public sealed class PanelLayoutComposer
{
    public UiElementBuilder Compose(
        PanelLayoutControl root,
        IPanelLayoutBindingScope scope,
        PanelLayoutImageSourceResolver imageSourceResolver)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(imageSourceResolver);
        return ComposeControl(root, scope, imageSourceResolver, listComposer: null)
            ?? throw new InvalidOperationException("Panel layout root cannot be hidden.");
    }

    public UiElementBuilder ComposeControls(
        IReadOnlyList<PanelLayoutControl> controls,
        IPanelLayoutBindingScope scope,
        PanelLayoutImageSourceResolver imageSourceResolver,
        Func<PanelLayoutControl, UiElementBuilder>? listComposer = null)
    {
        ArgumentNullException.ThrowIfNull(controls);
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(imageSourceResolver);

        var children = new List<UiElementBuilder>(controls.Count);
        for (int i = 0; i < controls.Count; i++)
        {
            UiElementBuilder? child = ComposeControl(controls[i], scope, imageSourceResolver, listComposer);
            if (child != null)
            {
                children.Add(child);
            }
        }

        return new UiElementBuilder(UiNodeKind.Container)
            .Column()
            .Class("layout")
            .WidthPercent(100f)
            .Gap(6)
            .Children(children.ToArray());
    }

    private UiElementBuilder? ComposeControl(
        PanelLayoutControl control,
        IPanelLayoutBindingScope scope,
        PanelLayoutImageSourceResolver imageSourceResolver,
        Func<PanelLayoutControl, UiElementBuilder>? listComposer)
    {
        if (!string.IsNullOrWhiteSpace(control.VisibleWhenNotEmpty) &&
            !scope.IsPresent(control.VisibleWhenNotEmpty))
        {
            return null;
        }

        return control.Type switch
        {
            PanelLayoutControlType.Label => BuildLabel(control, scope),
            PanelLayoutControlType.ProgressBar => BuildProgressBar(control, scope),
            PanelLayoutControlType.Badge => BuildBadge(control, scope),
            PanelLayoutControlType.List => listComposer?.Invoke(control)
                ?? throw new InvalidOperationException("Panel layout list control requires a list composer."),
            PanelLayoutControlType.Row => BuildContainer(control, scope, imageSourceResolver, row: true, listComposer),
            PanelLayoutControlType.Column => BuildContainer(control, scope, imageSourceResolver, row: false, listComposer),
            PanelLayoutControlType.Image => BuildImage(control, scope, imageSourceResolver),
            PanelLayoutControlType.RichText => BuildRichText(control, scope),
            PanelLayoutControlType.Repeater => BuildRepeater(control, scope, imageSourceResolver, listComposer),
            _ => throw new InvalidOperationException($"Panel layout control type '{control.Type}' is not supported.")
        };
    }

    private UiElementBuilder BuildContainer(
        PanelLayoutControl control,
        IPanelLayoutBindingScope scope,
        PanelLayoutImageSourceResolver imageSourceResolver,
        bool row,
        Func<PanelLayoutControl, UiElementBuilder>? listComposer)
    {
        var children = new List<UiElementBuilder>(control.Children.Count);
        for (int i = 0; i < control.Children.Count; i++)
        {
            UiElementBuilder? child = ComposeControl(control.Children[i], scope, imageSourceResolver, listComposer);
            if (child != null)
            {
                children.Add(child);
            }
        }

        UiElementBuilder builder = new UiElementBuilder(UiNodeKind.Container);
        builder = row ? builder.Row() : builder.Column();
        return ApplyCommon(builder.Children(children.ToArray()), control, scope);
    }

    private static UiElementBuilder BuildLabel(
        PanelLayoutControl control,
        IPanelLayoutBindingScope scope)
    {
        string text = control.Text ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(control.Bind))
        {
            text = scope.ReadText(control.Bind);
        }

        if (!string.IsNullOrEmpty(control.Prefix))
        {
            text = control.Prefix + text;
        }

        UiElementBuilder builder = new UiElementBuilder(UiNodeKind.Text)
            .Class("control-label")
            .Text(text)
            .FontSize(control.FontSize ?? 14)
            .Color(new UiColor(230, 230, 230));
        if (control.Bold)
        {
            builder = builder.Bold();
        }

        return ApplyCommon(builder, control, scope, "label");
    }

    private static UiElementBuilder BuildProgressBar(
        PanelLayoutControl control,
        IPanelLayoutBindingScope scope)
    {
        float current;
        float max;
        float ratio;
        if (!string.IsNullOrWhiteSpace(control.Bind))
        {
            ratio = Math.Clamp(scope.ReadFloat(control.Bind), 0f, 1f);
            current = ratio;
            max = 1f;
        }
        else
        {
            current = scope.ReadFloat(control.Current!);
            max = MathF.Max(0.0001f, scope.ReadFloat(control.Max!));
            ratio = Math.Clamp(current / max, 0f, 1f);
        }
        float fillPercent = MathF.Max(0f, ratio * 100f);

        UiElementBuilder builder = new UiElementBuilder(UiNodeKind.Container)
            .Column()
            .Class("control-progress")
            .WidthPercent(100f)
            .Gap(2)
            .Children(
                new UiElementBuilder(UiNodeKind.Text)
                    .Class("progress-caption")
                    .Text($"{current:F0} / {max:F0}")
                    .FontSize(11)
                    .Color(new UiColor(200, 200, 200)),
                new UiElementBuilder(UiNodeKind.Container)
                    .Row()
                    .Class("progress-track")
                    .WidthPercent(100f)
                    .Height(10)
                    .Background(new UiColor(40, 40, 55, 255))
                    .Radius(4)
                    .Children(
                        new UiElementBuilder(UiNodeKind.Container)
                            .Class("progress-fill")
                            .Class("progress-fill-health")
                            .WidthPercent(fillPercent)
                            .Height(10)
                            .Background(new UiColor(255, 68, 68, 255))
                            .Radius(4)));
        return ApplyCommon(builder, control, scope, "progress-bar");
    }

    private static UiElementBuilder? BuildBadge(
        PanelLayoutControl control,
        IPanelLayoutBindingScope scope)
    {
        bool flag = scope.ReadBool(control.Bind ?? string.Empty);
        if (control.ShowWhen.HasValue && flag != control.ShowWhen.Value)
        {
            return null;
        }

        if (!control.ShowWhen.HasValue && !flag)
        {
            return null;
        }

        UiElementBuilder builder = new UiElementBuilder(UiNodeKind.Text)
            .Class("control-badge")
            .Text(control.Text ?? control.Bind ?? "!")
            .FontSize(11)
            .Bold()
            .Color(new UiColor(255, 210, 80));
        return ApplyCommon(builder, control, scope, "badge");
    }

    private static UiElementBuilder BuildRichText(
        PanelLayoutControl control,
        IPanelLayoutBindingScope scope)
    {
        if (string.IsNullOrWhiteSpace(control.Bind) || string.IsNullOrWhiteSpace(control.TextRunsBind))
        {
            throw new InvalidOperationException("RichText control requires bind and textRunsBind.");
        }

        IReadOnlyList<PresentationTextRun> sourceRuns = scope.ReadTextRuns(control.TextRunsBind);
        var runs = new UiStyledTextRun[sourceRuns.Count];
        for (int i = 0; i < sourceRuns.Count; i++)
        {
            PresentationTextStyleOverride style = sourceRuns[i].Style;
            runs[i] = new UiStyledTextRun(
                sourceRuns[i].Text,
                style.Bold,
                style.Italic,
                style.HasColor,
                style.HasColor ? new UiColor(style.R, style.G, style.B, style.A) : default);
        }

        UiElementBuilder builder = Ui.Text(scope.ReadText(control.Bind))
            .TextRuns(runs)
            .WhiteSpace(UiWhiteSpace.Normal);
        if (control.FontSize.HasValue)
        {
            builder = builder.FontSize(control.FontSize.Value);
        }

        if (control.Bold)
        {
            builder = builder.Bold();
        }

        return ApplyCommon(builder, control, scope);
    }

    private static UiElementBuilder BuildImage(
        PanelLayoutControl control,
        IPanelLayoutBindingScope scope,
        PanelLayoutImageSourceResolver imageSourceResolver)
    {
        string imageReference = !string.IsNullOrWhiteSpace(control.Src)
            ? control.Src
            : !string.IsNullOrWhiteSpace(control.Bind)
                ? scope.ReadText(control.Bind)
                : string.Empty;
        if (string.IsNullOrWhiteSpace(imageReference))
        {
            throw new InvalidOperationException(
                "Panel image control resolved an empty image reference (src/bind).");
        }

        string source = imageSourceResolver(imageReference);
        if (string.IsNullOrWhiteSpace(source))
        {
            throw new InvalidOperationException(
                $"Panel image source resolver returned an empty source for '{imageReference}'.");
        }

        float? width = ResolveDimension(control.Width, control.WidthBind, scope);
        float? height = ResolveDimension(control.Height, control.HeightBind, scope);
        if (!width.HasValue)
        {
            throw new InvalidOperationException("Panel image control missing width/widthBind.");
        }

        if (!height.HasValue)
        {
            throw new InvalidOperationException("Panel image control missing height/heightBind.");
        }

        UiElementBuilder builder = Ui.Image(source)
            .Class("control-image")
            .Width(width.Value)
            .Height(height.Value)
            .FlexShrink(0f);
        if (!string.IsNullOrWhiteSpace(control.ObjectFit))
        {
            builder = builder.ObjectFit(ParseObjectFit(control.ObjectFit));
        }

        return ApplyCommon(builder, control, scope);
    }

    private UiElementBuilder BuildRepeater(
        PanelLayoutControl control,
        IPanelLayoutBindingScope scope,
        PanelLayoutImageSourceResolver imageSourceResolver,
        Func<PanelLayoutControl, UiElementBuilder>? listComposer)
    {
        if (string.IsNullOrWhiteSpace(control.Bind))
        {
            throw new InvalidOperationException("Repeater control requires bind.");
        }

        IReadOnlyList<IPanelLayoutBindingScope> items = scope.ReadList(control.Bind);
        var children = new List<UiElementBuilder>(items.Count * Math.Max(1, control.Children.Count));
        for (int itemIndex = 0; itemIndex < items.Count; itemIndex++)
        {
            for (int childIndex = 0; childIndex < control.Children.Count; childIndex++)
            {
                UiElementBuilder? child = ComposeControl(
                    control.Children[childIndex],
                    items[itemIndex],
                    imageSourceResolver,
                    listComposer);
                if (child != null)
                {
                    children.Add(child);
                }
            }
        }

        return ApplyCommon(
            new UiElementBuilder(UiNodeKind.Container).Column().Children(children.ToArray()),
            control,
            scope);
    }

    private static UiElementBuilder ApplyCommon(
        UiElementBuilder builder,
        PanelLayoutControl control,
        IPanelLayoutBindingScope scope,
        string? defaultClass = null)
    {
        if (!string.IsNullOrWhiteSpace(defaultClass) && string.IsNullOrWhiteSpace(control.ClassName))
        {
            builder = builder.Class(defaultClass);
        }

        if (!string.IsNullOrWhiteSpace(control.ClassName))
        {
            builder = builder.Classes(control.ClassName.Split(
                ' ',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }

        if (!string.IsNullOrWhiteSpace(control.ClassBind))
        {
            string classNames = scope.ReadText(control.ClassBind);
            if (!string.IsNullOrWhiteSpace(classNames))
            {
                builder = builder.Classes(classNames.Split(
                    ' ',
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            }
        }

        if (control.Gap.HasValue)
        {
            builder = builder.Gap(control.Gap.Value);
        }

        if (!string.IsNullOrWhiteSpace(control.Align))
        {
            builder = builder.Align(ParseAlign(control.Align));
        }

        if (!string.IsNullOrWhiteSpace(control.Justify))
        {
            builder = builder.Justify(ParseJustify(control.Justify));
        }

        float? width = ResolveDimension(control.Width, control.WidthBind, scope);
        float? height = ResolveDimension(control.Height, control.HeightBind, scope);
        if (width.HasValue)
        {
            builder = builder.Width(width.Value);
        }

        if (height.HasValue)
        {
            builder = builder.Height(height.Value);
        }

        if (!string.IsNullOrWhiteSpace(control.ColorBind))
        {
            string value = scope.ReadText(control.ColorBind);
            if (!string.IsNullOrWhiteSpace(value))
            {
                builder = builder.Color(ParseColor(value, control.ColorBind));
            }
        }

        if (!string.IsNullOrWhiteSpace(control.BackgroundBind))
        {
            string value = scope.ReadText(control.BackgroundBind);
            if (!string.IsNullOrWhiteSpace(value))
            {
                builder = builder.Background(ParseColor(value, control.BackgroundBind));
            }
        }

        return builder;
    }

    private static float? ResolveDimension(
        float? authored,
        string? bind,
        IPanelLayoutBindingScope scope)
    {
        return string.IsNullOrWhiteSpace(bind) ? authored : scope.ReadFloat(bind);
    }

    private static UiObjectFit ParseObjectFit(string value)
    {
        return value switch
        {
            "fill" => UiObjectFit.Fill,
            "contain" => UiObjectFit.Contain,
            "cover" => UiObjectFit.Cover,
            "none" => UiObjectFit.None,
            "scale-down" => UiObjectFit.ScaleDown,
            _ => throw new InvalidOperationException($"Unknown objectFit '{value}'.")
        };
    }

    private static UiAlignItems ParseAlign(string value)
    {
        return value switch
        {
            "start" => UiAlignItems.Start,
            "center" => UiAlignItems.Center,
            "end" => UiAlignItems.End,
            "stretch" => UiAlignItems.Stretch,
            _ => throw new InvalidOperationException($"Unknown align '{value}'.")
        };
    }

    private static UiJustifyContent ParseJustify(string value)
    {
        return value switch
        {
            "start" => UiJustifyContent.Start,
            "center" => UiJustifyContent.Center,
            "end" => UiJustifyContent.End,
            "space-between" => UiJustifyContent.SpaceBetween,
            "space-around" => UiJustifyContent.SpaceAround,
            "space-evenly" => UiJustifyContent.SpaceEvenly,
            _ => throw new InvalidOperationException($"Unknown justify '{value}'.")
        };
    }

    private static UiColor ParseColor(string value, string bind)
    {
        if (!UiColor.TryParse(value, out UiColor color))
        {
            throw new InvalidOperationException($"Binding '{bind}' returned invalid color '{value}'.");
        }

        return color;
    }
}
