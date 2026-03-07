using System.Globalization;
using System.Text.Json;
using Ludots.UI.Runtime.Diff;
using SkiaSharp;

namespace Ludots.UI.Runtime.Serialization;

public sealed class UiSceneDiffJsonSerializer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public string Serialize(UiScene scene, float width, float height)
    {
        ArgumentNullException.ThrowIfNull(scene);
        scene.Layout(width, height);

        UiSceneDiff diff = scene.CreateFullDiff();
        SceneDiffPayload payload = new(
            diff.Kind.ToString(),
            diff.Snapshot.Version,
            width,
            height,
            diff.Snapshot.Root != null ? MapNode(diff.Snapshot.Root) : null);
        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    private static NodePayload MapNode(UiNodeDiff node)
    {
        return new NodePayload(
            node.Id.Value,
            node.Kind.ToString(),
            node.TagName,
            node.ElementId,
            node.ClassNames.ToArray(),
            node.TextContent,
            new StylePayload(
                node.Style.Display.ToString(),
                node.Style.FlexDirection.ToString(),
                node.Style.JustifyContent.ToString(),
                node.Style.AlignItems.ToString(),
                node.Style.Width.ToString(),
                node.Style.Height.ToString(),
                node.Style.MinWidth.ToString(),
                node.Style.MinHeight.ToString(),
                node.Style.MaxWidth.ToString(),
                node.Style.MaxHeight.ToString(),
                node.Style.Gap,
                node.Style.Margin,
                node.Style.Padding,
                node.Style.BorderWidth,
                node.Style.BorderRadius,
                node.Style.OutlineWidth,
                ToCss(node.Style.BackgroundColor),
                node.Style.BackgroundGradient != null ? MapGradient(node.Style.BackgroundGradient) : null,
                ToCss(node.Style.BorderColor),
                ToCss(node.Style.OutlineColor),
                node.Style.BoxShadow != null ? MapShadow(node.Style.BoxShadow.Value) : null,
                ToCss(node.Style.Color),
                node.Style.TextShadow != null ? MapShadow(node.Style.TextShadow.Value) : null,
                node.Style.FontSize,
                node.Style.FontFamily,
                node.Style.Bold,
                node.Style.WhiteSpace.ToString(),
                node.Style.Opacity,
                node.Style.Visible,
                node.Style.ClipContent),
            new RectPayload(node.LayoutRect.X, node.LayoutRect.Y, node.LayoutRect.Width, node.LayoutRect.Height),
            node.Children.Select(MapNode).ToArray());
    }

    private static string ToCss(SKColor color)
    {
        return $"rgba({color.Red},{color.Green},{color.Blue},{(color.Alpha / 255f).ToString("0.###", CultureInfo.InvariantCulture)})";
    }

    private static ShadowPayload MapShadow(UiShadow shadow)
    {
        return new ShadowPayload(shadow.OffsetX, shadow.OffsetY, shadow.BlurRadius, shadow.SpreadRadius, ToCss(shadow.Color));
    }

    private static GradientPayload MapGradient(UiLinearGradient gradient)
    {
        return new GradientPayload(gradient.AngleDegrees, gradient.Stops.Select(static stop => new GradientStopPayload(stop.Position, ToCss(stop.Color))).ToArray());
    }

    private sealed record SceneDiffPayload(string Kind, long Version, float ViewportWidth, float ViewportHeight, NodePayload? Root);

    private sealed record NodePayload(
        int Id,
        string Kind,
        string TagName,
        string? ElementId,
        IReadOnlyList<string> ClassNames,
        string? TextContent,
        StylePayload Style,
        RectPayload LayoutRect,
        IReadOnlyList<NodePayload> Children);

    private sealed record StylePayload(
        string Display,
        string FlexDirection,
        string JustifyContent,
        string AlignItems,
        string Width,
        string Height,
        string MinWidth,
        string MinHeight,
        string MaxWidth,
        string MaxHeight,
        float Gap,
        UiThickness Margin,
        UiThickness Padding,
        float BorderWidth,
        float BorderRadius,
        float OutlineWidth,
        string BackgroundColor,
        GradientPayload? BackgroundGradient,
        string BorderColor,
        string OutlineColor,
        ShadowPayload? BoxShadow,
        string Color,
        ShadowPayload? TextShadow,
        float FontSize,
        string? FontFamily,
        bool Bold,
        string WhiteSpace,
        float Opacity,
        bool Visible,
        bool ClipContent);

    private sealed record RectPayload(float X, float Y, float Width, float Height);

    private sealed record ShadowPayload(float OffsetX, float OffsetY, float BlurRadius, float SpreadRadius, string Color);

    private sealed record GradientPayload(float AngleDegrees, IReadOnlyList<GradientStopPayload> Stops);

    private sealed record GradientStopPayload(float Position, string Color);
}
