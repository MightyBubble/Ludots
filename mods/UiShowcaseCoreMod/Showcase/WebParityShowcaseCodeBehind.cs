using System;
using System.Collections.Generic;
using System.Reflection;
using Ludots.UI.Compose;
using Ludots.UI.HtmlEngine.Markup;
using Ludots.UI.Runtime;
using Ludots.UI.Runtime.Actions;
using Ludots.UI.Surface;

namespace UiShowcaseCoreMod.Showcase;

internal sealed class WebParityShowcaseCodeBehind
{
	private readonly UiMarkupLoader _loader = new UiMarkupLoader();
	private readonly IUiTextMeasurer _textMeasurer;
	private readonly IUiImageSizeProvider _imageSizeProvider;
	private string _viewport = "desktop";
	private Action? _requestRebuild;

	internal WebParityShowcaseCodeBehind(IUiTextMeasurer textMeasurer, IUiImageSizeProvider imageSizeProvider)
	{
		_textMeasurer = textMeasurer ?? throw new ArgumentNullException(nameof(textMeasurer));
		_imageSizeProvider = imageSizeProvider ?? throw new ArgumentNullException(nameof(imageSizeProvider));
	}

	internal UiScene BuildScene()
	{
		UiDocument document = _loader.LoadDocument(BuildHtml(), UiShowcaseAssets.GetWebParityShowcaseCss());
		UiScene scene = new UiScene(_textMeasurer, _imageSizeProvider);
		scene.MountDocument(document);
		MarkupBinder.Bind(scene, this);
		return scene;
	}

	internal UiSurfaceContribution CreateContribution(Action requestRebuild)
	{
		_requestRebuild = requestRebuild ?? throw new ArgumentNullException(nameof(requestRebuild));
		UiStyleSheet sheet = UiCssParser.ParseStyleSheet(UiShowcaseAssets.GetWebParityShowcaseCss());
		return UiSurfaceContribution.FromBuilder(BuildRoot, styleSheets: new[] { sheet });
	}

	internal void ViewportDesktop(UiActionContext context) => ApplyViewport("desktop", context);

	internal void ViewportTablet(UiActionContext context) => ApplyViewport("tablet", context);

	internal void ViewportPhone(UiActionContext context) => ApplyViewport("phone", context);

	private void ApplyViewport(string viewport, UiActionContext context)
	{
		if (string.Equals(_viewport, viewport, StringComparison.Ordinal))
		{
			return;
		}
		_viewport = viewport;
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
		UiDocument document = _loader.LoadDocument(BuildHtml(), UiShowcaseAssets.GetWebParityShowcaseCss());
		context.Scene.MountDocument(document);
		MarkupBinder.Bind(context.Scene, this);
	}

	private UiElementBuilder BuildRoot()
	{
		UiDocument document = _loader.LoadDocument(BuildHtml(), UiShowcaseAssets.GetWebParityShowcaseCss());
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
		bool desktop = _viewport == "desktop";
		bool tablet = _viewport == "tablet";
		bool phone = _viewport == "phone";
		return UiShowcaseAssets.RenderTemplate(
			UiShowcaseAssets.GetWebParityShowcaseHtmlTemplate(),
			new Dictionary<string, string>(StringComparer.Ordinal)
			{
				["desktop_class"] = desktop ? "active" : string.Empty,
				["tablet_class"] = tablet ? "active" : string.Empty,
				["phone_class"] = phone ? "active" : string.Empty,
				["stage_class"] = "vp-" + _viewport,
				["viewport_label"] = desktop ? "桌面 1280×720" : tablet ? "平板 900×700" : "手机 390×844",
				["parity_badge"] = "布局已与浏览器参考对齐",
				["point_1"] = "菜单卡片、四宫格按钮、右侧任务栏相对位置一致",
				["point_2"] = "换分辨率后宽度高度跟着变，结构不塌",
				["point_3"] = "静态颜色、边框、间距来自同一份 HTML/CSS"
			});
	}
}
