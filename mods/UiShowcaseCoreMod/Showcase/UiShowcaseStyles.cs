using System;
using System.Collections.Generic;
using Ludots.UI.HtmlEngine.Markup;
using Ludots.UI.Runtime;

namespace UiShowcaseCoreMod.Showcase;

public static class UiShowcaseStyles
{
	private static readonly Lazy<UiStyleSheet> AuthoringStyleSheet = new Lazy<UiStyleSheet>(() => UiCssParser.ParseStyleSheet(BuildAuthoringCss()));

	private static readonly Lazy<string> AuthoringCss = new Lazy<string>(() => UiShowcaseAssets.RenderTemplate(UiShowcaseAssets.GetAuthoringCss(), new Dictionary<string, string>(StringComparer.Ordinal) { ["badge_svg_data_uri"] = UiShowcaseImageAssets.BadgeSvgDataUri }));

	public static UiStyleSheet BuildAuthoringStyleSheet()
	{
		return AuthoringStyleSheet.Value;
	}

	public static string BuildAuthoringCss()
	{
		return AuthoringCss.Value;
	}
}
