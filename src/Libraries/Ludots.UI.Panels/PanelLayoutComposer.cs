using System;
using System.Collections.Generic;
using Ludots.Core.UI.PanelProjection;
using Ludots.UI.Compose;
using Ludots.UI.Runtime;

namespace Ludots.UI.Panels;

public interface IPanelLayoutBindingScope
{
    string ReadText(string bind);
    float ReadFloat(string bind);
    bool ReadBool(string bind);
}

public sealed class PanelLayoutComposer
{
    private const float PanelWidth = 280f;

    public UiElementBuilder ComposeControls(
        IReadOnlyList<PanelLayoutControl> controls,
        IPanelLayoutBindingScope scope,
        Func<PanelLayoutControl, UiElementBuilder>? listComposer = null)
    {
        ArgumentNullException.ThrowIfNull(controls);
        ArgumentNullException.ThrowIfNull(scope);

        var children = new List<UiElementBuilder>(controls.Count);
        for (int i = 0; i < controls.Count; i++)
        {
            UiElementBuilder? child = ComposeControl(controls[i], scope, listComposer);
            if (child != null)
            {
                children.Add(child);
            }
        }

        return new UiElementBuilder(UiNodeKind.Container)
            .Column()
            .Class("layout")
            .Gap(6)
            .Children(children.ToArray());
    }

    private static UiElementBuilder? ComposeControl(
        PanelLayoutControl control,
        IPanelLayoutBindingScope scope,
        Func<PanelLayoutControl, UiElementBuilder>? listComposer)
    {
        return control.Type switch
        {
            PanelLayoutControlType.Label => BuildLabel(control, scope),
            PanelLayoutControlType.ProgressBar => BuildProgressBar(control, scope),
            PanelLayoutControlType.Badge => BuildBadge(control, scope),
            PanelLayoutControlType.List => listComposer?.Invoke(control)
                ?? throw new InvalidOperationException("Panel layout list control requires a list composer."),
            _ => throw new InvalidOperationException($"Panel layout control type '{control.Type}' is not supported.")
        };
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

        return new UiElementBuilder(UiNodeKind.Text)
            .Class("control-label")
            .Class(control.ClassName ?? "label")
            .Text(text)
            .FontSize(14)
            .Color(new UiColor(230, 230, 230));
    }

    private static UiElementBuilder BuildProgressBar(
        PanelLayoutControl control,
        IPanelLayoutBindingScope scope)
    {
        float current = scope.ReadFloat(control.Current!);
        float max = MathF.Max(0.0001f, scope.ReadFloat(control.Max!));
        float ratio = Math.Clamp(current / max, 0f, 1f);
        float trackWidth = PanelWidth - 48f;
        float fillWidth = MathF.Max(2f, trackWidth * ratio);

        return new UiElementBuilder(UiNodeKind.Container)
            .Column()
            .Class("control-progress")
            .Class(control.ClassName ?? "progress-bar")
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
                    .Width(trackWidth)
                    .Height(10)
                    .Background(new UiColor(40, 40, 55, 255))
                    .Radius(4)
                    .Children(
                        new UiElementBuilder(UiNodeKind.Container)
                            .Class("progress-fill")
                            .Class("progress-fill-health")
                            .Width(fillWidth)
                            .Height(10)
                            .Background(new UiColor(255, 68, 68, 255))
                            .Radius(4)));
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

        return new UiElementBuilder(UiNodeKind.Text)
            .Class("control-badge")
            .Class(control.ClassName ?? "badge")
            .Text(control.Text ?? control.Bind ?? "!")
            .FontSize(11)
            .Bold()
            .Color(new UiColor(255, 210, 80));
    }
}
