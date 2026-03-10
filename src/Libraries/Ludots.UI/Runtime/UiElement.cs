using System;
using System.Collections.Generic;
using System.Linq;

namespace Ludots.UI.Runtime;

public sealed class UiElement
{
	private readonly List<UiElement> _children = new List<UiElement>();

	public string TagName { get; }

	public UiNodeKind Kind { get; set; }

	public UiElement? Parent { get; private set; }

	public string? TextContent { get; set; }

	public UiAttributeBag Attributes { get; } = new UiAttributeBag();

	public UiStyleDeclaration InlineStyle { get; } = new UiStyleDeclaration();

	public IReadOnlyList<UiElement> Children => _children;

	public string? ElementId => Attributes["id"];

	public IReadOnlyList<string> ClassNames => Attributes.GetClassList();

	public UiElement(string tagName, UiNodeKind kind = UiNodeKind.Container)
	{
		if (string.IsNullOrWhiteSpace(tagName))
		{
			throw new ArgumentException("Tag name is required.", "tagName");
		}
		TagName = tagName;
		Kind = kind;
	}

	public UiElement AddChild(UiElement child)
	{
		ArgumentNullException.ThrowIfNull(child, "child");
		child.Parent = this;
		_children.Add(child);
		return this;
	}

	public bool HasClass(string className)
	{
		return ClassNames.Contains<string>(className, StringComparer.OrdinalIgnoreCase);
	}
}
