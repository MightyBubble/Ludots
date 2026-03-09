using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using Ludots.UI.Runtime.Diff;
using SkiaSharp;

namespace Ludots.UI.Runtime.Serialization;

public sealed class UiSceneDiffJsonSerializer
{
	private sealed record SceneDiffPayload(string Kind, long Version, float ViewportWidth, float ViewportHeight, NodePayload? Root);

	private sealed record NodePayload(int Id, string Kind, string TagName, string? ElementId, IReadOnlyList<string> ClassNames, string? TextContent, string? ImageSource, StylePayload Style, RectPayload LayoutRect, float ScrollOffsetX, float ScrollOffsetY, float ScrollContentWidth, float ScrollContentHeight, IReadOnlyList<NodePayload> Children);

	private sealed record StylePayload(string Display, string Overflow, string FlexDirection, string JustifyContent, string AlignItems, string AlignContent, string FlexWrap, string Direction, string TextAlign, string ObjectFit, string Width, string Height, string MinWidth, string MinHeight, string MaxWidth, string MaxHeight, float Gap, float RowGap, float ColumnGap, UiThickness Margin, UiThickness Padding, UiThickness ImageSlice, float BorderWidth, float BorderRadius, float OutlineWidth, int ZIndex, string BackgroundColor, GradientPayload? BackgroundGradient, string BorderColor, string OutlineColor, ShadowPayload? BoxShadow, float FilterBlurRadius, float BackdropBlurRadius, string Color, ShadowPayload? TextShadow, float FontSize, string? FontFamily, bool Bold, string WhiteSpace, string Transform, float Opacity, bool Visible, bool ClipContent);

	private sealed record RectPayload(float X, float Y, float Width, float Height);

	private sealed record ShadowPayload(float OffsetX, float OffsetY, float BlurRadius, float SpreadRadius, string Color);

	private sealed record GradientPayload(float AngleDegrees, IReadOnlyList<GradientStopPayload> Stops);

	private sealed record GradientStopPayload(float Position, string Color);

	private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		WriteIndented = false
	};

	public string Serialize(UiScene scene, float width, float height)
	{
		ArgumentNullException.ThrowIfNull(scene, "scene");
		scene.Layout(width, height);
		UiSceneDiff uiSceneDiff = scene.CreateFullDiff();
		SceneDiffPayload value = new SceneDiffPayload(uiSceneDiff.Kind.ToString(), uiSceneDiff.Snapshot.Version, width, height, (uiSceneDiff.Snapshot.Root != null) ? MapNode(uiSceneDiff.Snapshot.Root) : null);
		return JsonSerializer.Serialize(value, JsonOptions);
	}

	private static NodePayload MapNode(UiNodeDiff node)
	{
		return new NodePayload(node.Id.Value, node.Kind.ToString(), node.TagName, node.ElementId, node.ClassNames.ToArray(), node.TextContent, node.ImageSource, new StylePayload(node.Style.Display.ToString(), node.Style.Overflow.ToString(), node.Style.FlexDirection.ToString(), node.Style.JustifyContent.ToString(), node.Style.AlignItems.ToString(), node.Style.AlignContent.ToString(), node.Style.FlexWrap.ToString(), node.Style.Direction.ToString(), node.Style.TextAlign.ToString(), node.Style.ObjectFit.ToString(), node.Style.Width.ToString(), node.Style.Height.ToString(), node.Style.MinWidth.ToString(), node.Style.MinHeight.ToString(), node.Style.MaxWidth.ToString(), node.Style.MaxHeight.ToString(), node.Style.Gap, node.Style.RowGap, node.Style.ColumnGap, node.Style.Margin, node.Style.Padding, node.Style.ImageSlice, node.Style.BorderWidth, node.Style.BorderRadius, node.Style.OutlineWidth, node.Style.ZIndex, ToCss(node.Style.BackgroundColor), (node.Style.BackgroundGradient != null) ? MapGradient(node.Style.BackgroundGradient) : null, ToCss(node.Style.BorderColor), ToCss(node.Style.OutlineColor), node.Style.BoxShadow.HasValue ? MapShadow(node.Style.BoxShadow.Value) : null, node.Style.FilterBlurRadius, node.Style.BackdropBlurRadius, ToCss(node.Style.Color), node.Style.TextShadow.HasValue ? MapShadow(node.Style.TextShadow.Value) : null, node.Style.FontSize, node.Style.FontFamily, node.Style.Bold, node.Style.WhiteSpace.ToString(), node.Style.Transform.ToString(), node.Style.Opacity, node.Style.Visible, node.Style.ClipContent), new RectPayload(node.LayoutRect.X, node.LayoutRect.Y, node.LayoutRect.Width, node.LayoutRect.Height), node.ScrollOffsetX, node.ScrollOffsetY, node.ScrollContentWidth, node.ScrollContentHeight, node.Children.Select(MapNode).ToArray());
	}

	private static string ToCss(SKColor color)
	{
		return $"rgba({color.Red},{color.Green},{color.Blue},{((float)(int)color.Alpha / 255f).ToString("0.###", CultureInfo.InvariantCulture)})";
	}

	private static ShadowPayload MapShadow(UiShadow shadow)
	{
		return new ShadowPayload(shadow.OffsetX, shadow.OffsetY, shadow.BlurRadius, shadow.SpreadRadius, ToCss(shadow.Color));
	}

	private static GradientPayload MapGradient(UiLinearGradient gradient)
	{
		return new GradientPayload(gradient.AngleDegrees, gradient.Stops.Select((UiGradientStop stop) => new GradientStopPayload(stop.Position, ToCss(stop.Color))).ToArray());
	}
}
