using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace UiShowcaseCoreMod.Showcase;

internal static class UiShowcaseAssets
{
	private static readonly Assembly ResourceAssembly = typeof(UiShowcaseAssets).Assembly;

	private static readonly string ResourceRoot = ResourceAssembly.GetName().Name + ".Assets.Showcase.";

	private static readonly Lazy<string> AuthoringCss = new Lazy<string>(() => ReadRequiredText("showcase_authoring.css"));

	private static readonly Lazy<string> MarkupShowcaseCss = new Lazy<string>(() => ReadRequiredText("markup_showcase.css"));

	private static readonly Lazy<string> MarkupShowcaseHtml = new Lazy<string>(() => ReadRequiredText("markup_showcase.html"));

	private static readonly Lazy<string> ShowcaseBadgeSvg = new Lazy<string>(() => ReadRequiredText("showcase_badge.svg"));

	internal static string GetAuthoringCss()
	{
		return AuthoringCss.Value;
	}

	internal static string GetMarkupShowcaseCss()
	{
		return MarkupShowcaseCss.Value;
	}

	internal static string GetMarkupShowcaseHtmlTemplate()
	{
		return MarkupShowcaseHtml.Value;
	}

	internal static string GetShowcaseBadgeSvg()
	{
		return ShowcaseBadgeSvg.Value;
	}

	internal static string RenderTemplate(string template, IReadOnlyDictionary<string, string> values)
	{
		ArgumentNullException.ThrowIfNull(template, "template");
		ArgumentNullException.ThrowIfNull(values, "values");
		string text = template;
		foreach (KeyValuePair<string, string> value in values)
		{
			text = text.Replace("{{" + value.Key + "}}", value.Value, StringComparison.Ordinal);
		}
		return text;
	}

	private static string ReadRequiredText(string fileName)
	{
		string text = ResourceRoot + fileName;
		using Stream stream = ResourceAssembly.GetManifestResourceStream(text);
		if (stream == null)
		{
			throw new InvalidOperationException("Embedded showcase asset '" + text + "' was not found.");
		}
		using StreamReader streamReader = new StreamReader(stream);
		return streamReader.ReadToEnd();
	}
}
