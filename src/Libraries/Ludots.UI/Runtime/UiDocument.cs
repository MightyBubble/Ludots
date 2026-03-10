using System;
using System.Collections.Generic;
using System.Linq;

namespace Ludots.UI.Runtime;

public sealed class UiDocument
{
	public string? Title { get; set; }

	public UiElement Root { get; }

	public List<UiStyleSheet> StyleSheets { get; } = new List<UiStyleSheet>();

	public string? ThemeKey { get; set; }

	public UiDocument(UiElement root)
	{
		Root = root ?? throw new ArgumentNullException("root");
	}

	public UiElement? QuerySelector(string selectorText)
	{
		return QuerySelectorAll(selectorText).FirstOrDefault();
	}

	public IReadOnlyList<UiElement> QuerySelectorAll(string selectorText)
	{
		IReadOnlyList<UiSelector> selectors = UiSelectorParser.ParseMany(selectorText);
		List<UiElement> list = new List<UiElement>();
		Traverse(Root, selectors, list);
		return list;
	}

	public UiElement? FindById(string elementId)
	{
		return QuerySelector("#" + elementId);
	}

	private static void Traverse(UiElement element, IReadOnlyList<UiSelector> selectors, List<UiElement> matches)
	{
		for (int i = 0; i < selectors.Count; i++)
		{
			if (UiElementSelectorMatcher.Matches(element, selectors[i]))
			{
				matches.Add(element);
				break;
			}
		}
		foreach (UiElement child in element.Children)
		{
			Traverse(child, selectors, matches);
		}
	}
}
