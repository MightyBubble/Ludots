using System;
using System.Collections.Generic;
using System.Linq;
using Ludots.UI.Runtime.Actions;

namespace Ludots.UI.Runtime;

public sealed class UiElement
{
	private readonly List<UiElement> _children = new List<UiElement>();

	private string? _textContent;

	private UiTextBinding? _textBinding;

	public string TagName { get; }

	public UiNodeKind Kind { get; set; }

	public UiElement? Parent { get; private set; }

	public string? TextContent
	{
		get => _textContent;
		set
		{
			if (value != null && TextBinding != null)
			{
				throw new InvalidOperationException($"<{TagName}> is text-bound and cannot also carry static text.");
			}
			_textContent = value;
		}
	}

	/// <summary>挂上后建出来的节点文字由它提供；与 <see cref="TextContent"/> 互斥。</summary>
	public UiTextBinding? TextBinding
	{
		get => _textBinding;
		set
		{
			if (value != null)
			{
				if (!string.IsNullOrWhiteSpace(_textContent))
				{
					throw new InvalidOperationException($"<{TagName}> carries static text '{_textContent}' and cannot also be text-bound.");
				}
				_textContent = null;
			}
			_textBinding = value;
		}
	}

	public IUiCanvasContent? CanvasContent { get; set; }

	public Action<UiActionContext>? ClickHandler { get; set; }

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

	public void ClearChildren()
	{
		foreach (UiElement child in _children)
		{
			child.Parent = null;
		}
		_children.Clear();
	}

	public bool HasClass(string className)
	{
		return ClassNames.Contains<string>(className, StringComparer.OrdinalIgnoreCase);
	}
}
