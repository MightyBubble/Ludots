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
	private string _size = "compact";
	private Action? _requestRebuild;

	internal NineSlicePanelShowcaseCodeBehind(IUiTextMeasurer textMeasurer, IUiImageSizeProvider imageSizeProvider)
	{
		_textMeasurer = textMeasurer ?? throw new ArgumentNullException(nameof(textMeasurer));
		_imageSizeProvider = imageSizeProvider ?? throw new ArgumentNullException(nameof(imageSizeProvider));
	}

	internal UiScene BuildScene()
	{
		UiDocument document = _loader.LoadDocument(BuildHtml(), UiShowcaseAssets.GetNineSlicePanelCss());
		UiScene scene = new UiScene(_textMeasurer, _imageSizeProvider);
		scene.MountDocument(document);
		MarkupBinder.Bind(scene, this);
		return scene;
	}

	internal UiSurfaceContribution CreateContribution(Action requestRebuild)
	{
		_requestRebuild = requestRebuild ?? throw new ArgumentNullException(nameof(requestRebuild));
		UiStyleSheet sheet = UiCssParser.ParseStyleSheet(UiShowcaseAssets.GetNineSlicePanelCss());
		return UiSurfaceContribution.FromBuilder(BuildRoot, styleSheets: new[] { sheet });
	}

	internal void SizeCompact(UiActionContext context) => ApplySize("compact", context);

	internal void SizeWide(UiActionContext context) => ApplySize("wide", context);

	internal void SizeTall(UiActionContext context) => ApplySize("tall", context);

	private void ApplySize(string size, UiActionContext context)
	{
		if (string.Equals(_size, size, StringComparison.Ordinal))
		{
			return;
		}
		_size = size;
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
		UiDocument document = _loader.LoadDocument(BuildHtml(), UiShowcaseAssets.GetNineSlicePanelCss());
		context.Scene.MountDocument(document);
		MarkupBinder.Bind(context.Scene, this);
	}

	private UiElementBuilder BuildRoot()
	{
		UiDocument document = _loader.LoadDocument(BuildHtml(), UiShowcaseAssets.GetNineSlicePanelCss());
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

	private string BuildHtml()
	{
		bool compact = _size == "compact";
		bool wide = _size == "wide";
		bool tall = _size == "tall";
		return UiShowcaseAssets.RenderTemplate(
			UiShowcaseAssets.GetNineSlicePanelHtmlTemplate(),
			new Dictionary<string, string>(StringComparer.Ordinal)
			{
				["compact_class"] = compact ? "active" : string.Empty,
				["wide_class"] = wide ? "active" : string.Empty,
				["tall_class"] = tall ? "active" : string.Empty,
				["size_class"] = "size-" + _size,
				["panel_frame_data_uri"] = UiShowcaseImageAssets.NineSlicePanelFrameDataUri,
				["button_frame_data_uri"] = UiShowcaseImageAssets.NineSliceButtonFrameDataUri
			});
	}
}
