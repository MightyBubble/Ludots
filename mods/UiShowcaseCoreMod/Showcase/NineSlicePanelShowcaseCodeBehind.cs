using System;
using System.Collections.Generic;
using System.Reflection;
using Ludots.UI.Compose;
using Ludots.UI.HtmlEngine.Markup;
using Ludots.UI.Runtime;
using Ludots.UI.Runtime.Actions;
using Ludots.UI.Surface;

namespace UiShowcaseCoreMod.Showcase;

internal sealed class NineSlicePanelShowcaseCodeBehind
{
	private readonly UiMarkupLoader _loader = new UiMarkupLoader();
	private readonly IUiTextMeasurer _textMeasurer;
	private readonly IUiImageSizeProvider _imageSizeProvider;
	private string _mode = "nine";
	private Action? _requestRebuild;

	internal NineSlicePanelShowcaseCodeBehind(IUiTextMeasurer textMeasurer, IUiImageSizeProvider imageSizeProvider)
	{
		_textMeasurer = textMeasurer ?? throw new ArgumentNullException(nameof(textMeasurer));
		_imageSizeProvider = imageSizeProvider ?? throw new ArgumentNullException(nameof(imageSizeProvider));
	}

	internal UiScene BuildScene()
	{
		UiDocument document = _loader.LoadDocument(BuildHtml(), BuildCss());
		UiScene scene = new UiScene(_textMeasurer, _imageSizeProvider);
		scene.MountDocument(document);
		MarkupBinder.Bind(scene, this);
		return scene;
	}

	internal UiSurfaceContribution CreateContribution(Action requestRebuild)
	{
		_requestRebuild = requestRebuild ?? throw new ArgumentNullException(nameof(requestRebuild));
		UiStyleSheet sheet = UiCssParser.ParseStyleSheet(BuildCss());
		return UiSurfaceContribution.FromBuilder(BuildRoot, styleSheets: new[] { sheet });
	}

	internal void ModeNine(UiActionContext context) => ApplyMode("nine", context);

	internal void ModeThree(UiActionContext context) => ApplyMode("three", context);

	internal void ModeTwo(UiActionContext context) => ApplyMode("two", context);

	internal void ModeFour(UiActionContext context) => ApplyMode("four", context);

	private void ApplyMode(string mode, UiActionContext context)
	{
		if (string.Equals(_mode, mode, StringComparison.Ordinal))
		{
			return;
		}
		_mode = mode;
		if (_requestRebuild != null)
		{
			_requestRebuild.Invoke();
			return;
		}
		Rebuild(context);
	}

	private void Rebuild(UiActionContext context)
	{
		context.Scene.Dispatcher.Reset();
		UiDocument document = _loader.LoadDocument(BuildHtml(), BuildCss());
		context.Scene.MountDocument(document);
		MarkupBinder.Bind(context.Scene, this);
	}

	private UiElementBuilder BuildRoot()
	{
		UiDocument document = _loader.LoadDocument(BuildHtml(), BuildCss());
		return BindActionsRecursive(document.Root);
	}

	private UiElementBuilder BindActionsRecursive(UiElement element)
	{
		UiElementBuilder builder = new UiElementBuilder(element.Kind, element.TagName);
		foreach (KeyValuePair<string, string> attribute in element.Attributes)
		{
			if (attribute.Key.Equals("id", StringComparison.OrdinalIgnoreCase))
			{
				builder.Id(attribute.Value);
			}
			else if (attribute.Key.Equals("class", StringComparison.OrdinalIgnoreCase))
			{
				builder.Classes(attribute.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
			}
			else if (!attribute.Key.Equals("ui-click", StringComparison.OrdinalIgnoreCase) &&
				!attribute.Key.Equals("data-click", StringComparison.OrdinalIgnoreCase))
			{
				builder.Attribute(attribute.Key, attribute.Value);
			}
		}
		if (!string.IsNullOrWhiteSpace(element.TextContent))
		{
			builder.Text(element.TextContent);
		}
		if (element.InlineStyle.Count > 0)
		{
			builder.InlineStyle(element.InlineStyle);
		}

		string? action = element.Attributes["ui-click"] ?? element.Attributes["data-click"];
		if (!string.IsNullOrWhiteSpace(action))
		{
			MethodInfo method = GetType().GetMethod(action, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
				?? throw new InvalidOperationException($"Code-behind method '{action}' was not found.");
			builder.OnClick(ctx => method.Invoke(this, new object[] { ctx }));
		}

		for (int i = 0; i < element.Children.Count; i++)
		{
			builder.Child(BindActionsRecursive(element.Children[i]));
		}
		return builder;
	}

	private string BuildCss()
	{
		return UiShowcaseAssets.RenderTemplate(
			UiShowcaseAssets.GetNineSlicePanelCss(),
			new Dictionary<string, string>(StringComparer.Ordinal)
			{
				["tile2_h_data_uri"] = UiShowcaseImageAssets.Tile2HorizontalDataUri,
				["tile2_v_data_uri"] = UiShowcaseImageAssets.Tile2VerticalDataUri,
				["tile4_data_uri"] = UiShowcaseImageAssets.Tile4OrnamentDataUri
			});
	}

	private string BuildHtml()
	{
		bool nine = _mode == "nine";
		bool three = _mode == "three";
		bool two = _mode == "two";
		bool four = _mode == "four";
		(string badge, string p1, string p2, string p3, string footnote) = DescribeMode(_mode);
		return UiShowcaseAssets.RenderTemplate(
			UiShowcaseAssets.GetNineSlicePanelHtmlTemplate(),
			new Dictionary<string, string>(StringComparer.Ordinal)
			{
				["nine_class"] = nine ? "active" : string.Empty,
				["three_class"] = three ? "active" : string.Empty,
				["two_class"] = two ? "active" : string.Empty,
				["four_class"] = four ? "active" : string.Empty,
				["nine_panel_class"] = nine ? "visible" : string.Empty,
				["three_panel_class"] = three ? "visible" : string.Empty,
				["two_panel_class"] = two ? "visible" : string.Empty,
				["four_panel_class"] = four ? "visible" : string.Empty,
				["panel_frame_data_uri"] = UiShowcaseImageAssets.NineSlicePanelFrameDataUri,
				["button_frame_data_uri"] = UiShowcaseImageAssets.NineSliceButtonFrameDataUri,
				["ribbon_frame_data_uri"] = UiShowcaseImageAssets.Slice3RibbonDataUri,
				["aside_badge"] = badge,
				["point_1"] = p1,
				["point_2"] = p2,
				["point_3"] = p3,
				["footnote"] = footnote
			});
	}

	private static (string badge, string p1, string p2, string p3, string footnote) DescribeMode(string mode) => mode switch
	{
		"three" => (
			"当前：三宫格",
			"短绶带 / 长绶带同一张图。",
			"两端徽记宽度钉死，只拉中间木纹。",
			"左右切边有值，上下切边为 0。",
			"玩法：对比短带与长带，徽记形状应一样。"),
		"two" => (
			"当前：二方连续",
			"上面横条：左右一节节接。",
			"左边竖条：上下一节节接。",
			"花纹连续铺，不是整图硬拉。",
			"玩法：看接缝是否自然，有没有被拉糊。"),
		"four" => (
			"当前：四方连续",
			"整面墙用同一块花纹铺满。",
			"左右上下四个方向都接缝。",
			"适合地砖、墙纸、盔甲鳞片底纹。",
			"玩法：扫一眼接缝，四边应对得上。"),
		_ => (
			"当前：九宫格",
			"卷宗四角金饰钉死不变形。",
			"木边与羊皮纸被拉开填满中间。",
			"按钮也是九宫格，四角应对称。",
			"玩法：看角是否匀称；再切其他模式对比铺法。")
	};
}
