using System;
using System.Collections.Generic;
using System.Linq;
using Ludots.UI.Runtime.Actions;

namespace Ludots.UI.Runtime;

public sealed class UiNode
{
	private UiNode[] _children;

	private List<UiActionHandle> _actionHandles;

	private string[] _classNames;

	private readonly List<UiTransitionChannelState> _transitionChannels = new List<UiTransitionChannelState>();

	private readonly List<UiAnimationChannelState> _animationChannels = new List<UiAnimationChannelState>();

	private bool _hasComputedStyle;

	public UiNodeId Id { get; }

	public UiNodeKind Kind { get; }

	public UiNode? Parent { get; private set; }

	public string TagName { get; private set; }

	public string? ElementId { get; private set; }

	public UiAttributeBag Attributes { get; private set; }

	public UiStyleDeclaration InlineStyle { get; private set; }

	public UiStyle LocalStyle { get; private set; }

	public UiStyle Style { get; private set; }

	public UiStyle RenderStyle { get; private set; }

	public UiPseudoState PseudoState { get; private set; }

	public UiRect LayoutRect { get; private set; }

	public float ScrollOffsetX { get; private set; }

	public float ScrollOffsetY { get; private set; }

	public float ScrollContentWidth { get; private set; }

	public float ScrollContentHeight { get; private set; }

	public string? TextContent { get; private set; }

	public UiCanvasContent? CanvasContent { get; private set; }

	public IReadOnlyList<string> ClassNames => _classNames;

	public IReadOnlyList<UiNode> Children => _children;

	public IReadOnlyList<UiActionHandle> ActionHandles => _actionHandles;

	public float MaxScrollX => Math.Max(0f, ScrollContentWidth - LayoutRect.Width);

	public float MaxScrollY => Math.Max(0f, ScrollContentHeight - LayoutRect.Height);

	public bool CanScrollHorizontally => Style.Overflow == UiOverflow.Scroll && MaxScrollX > 0.01f;

	public bool CanScrollVertically => Style.Overflow == UiOverflow.Scroll && MaxScrollY > 0.01f;

	public UiNode(UiNodeId id, UiNodeKind kind, UiStyle? style = null, string? textContent = null, IEnumerable<UiNode>? children = null, IEnumerable<UiActionHandle>? actionHandles = null, string? tagName = null, string? elementId = null, IEnumerable<string>? classNames = null, UiAttributeBag? attributes = null, UiStyleDeclaration? inlineStyle = null, UiCanvasContent? canvasContent = null)
	{
		if (!id.IsValid)
		{
			throw new ArgumentException("UiNodeId must be valid.", "id");
		}
		Id = id;
		Kind = kind;
		LocalStyle = style ?? UiStyle.Default;
		Style = LocalStyle;
		RenderStyle = LocalStyle;
		TextContent = textContent;
		TagName = (string.IsNullOrWhiteSpace(tagName) ? GetDefaultTagName(kind) : tagName);
		ElementId = ((!string.IsNullOrWhiteSpace(elementId)) ? elementId : LocalStyle.Id);
		Attributes = attributes ?? new UiAttributeBag();
		if (!string.IsNullOrWhiteSpace(ElementId))
		{
			Attributes["id"] = ElementId;
		}
		_classNames = classNames?.Where((string value) => !string.IsNullOrWhiteSpace(value)).Distinct<string>(StringComparer.OrdinalIgnoreCase).ToArray() ?? SplitClasses(LocalStyle.ClassName);
		if (_classNames.Length != 0)
		{
			Attributes["class"] = string.Join(' ', _classNames);
		}
		InlineStyle = inlineStyle ?? new UiStyleDeclaration();
		CanvasContent = canvasContent;
		_children = children?.ToArray() ?? Array.Empty<UiNode>();
		_actionHandles = actionHandles?.Where((UiActionHandle handle) => handle.IsValid).ToList() ?? new List<UiActionHandle>();
		for (int num = 0; num < _children.Length; num++)
		{
			_children[num].Parent = this;
		}
	}

	internal bool ApplyDefinitionFrom(UiNode template)
	{
		ArgumentNullException.ThrowIfNull(template, "template");
		bool changed = !HasEquivalentDefinition(template);
		TagName = template.TagName;
		ElementId = template.ElementId;
		Attributes = template.Attributes;
		InlineStyle = template.InlineStyle;
		LocalStyle = template.LocalStyle;
		TextContent = template.TextContent;
		CanvasContent = template.CanvasContent;
		_classNames = template._classNames;
		_actionHandles = template._actionHandles;
		return changed;
	}

	internal void ReplaceChildren(UiNode[] children)
	{
		ArgumentNullException.ThrowIfNull(children, "children");
		_children = children;
		for (int i = 0; i < _children.Length; i++)
		{
			_children[i].Parent = this;
		}
	}

	public bool HasClass(string className)
	{
		return _classNames.Contains<string>(className, StringComparer.OrdinalIgnoreCase);
	}

	internal void SetComputedStyle(UiStyle style, UiAnimationSpec? animation = null)
	{
		ArgumentNullException.ThrowIfNull(style, "style");
		UiStyle style2 = Style;
		Style = style;
		if (!_hasComputedStyle)
		{
			_transitionChannels.Clear();
			RestartAnimations(style, animation);
			RenderStyle = ComposeRenderStyle();
			_hasComputedStyle = true;
		}
		else if (HasMeaningfulStyleChange(style2, style))
		{
			BeginVisualTransitions(style2, style);
			RestartAnimations(style, animation);
			RenderStyle = ComposeRenderStyle();
		}
	}

	internal bool AdvanceTransitions(float deltaSeconds)
	{
		if (deltaSeconds <= 0f)
		{
			return false;
		}
		for (int num = _transitionChannels.Count - 1; num >= 0; num--)
		{
			UiTransitionChannelState uiTransitionChannelState = _transitionChannels[num];
			uiTransitionChannelState.Advance(deltaSeconds);
			if (uiTransitionChannelState.IsCompleted)
			{
				_transitionChannels.RemoveAt(num);
			}
		}
		for (int num2 = _animationChannels.Count - 1; num2 >= 0; num2--)
		{
			UiAnimationChannelState uiAnimationChannelState = _animationChannels[num2];
			uiAnimationChannelState.Advance(deltaSeconds);
			if (uiAnimationChannelState.IsDiscardable)
			{
				_animationChannels.RemoveAt(num2);
			}
		}
		UiStyle uiStyle = ComposeRenderStyle();
		if (object.Equals(RenderStyle, uiStyle))
		{
			return false;
		}
		RenderStyle = uiStyle;
		return true;
	}

	internal void ResetVisualState()
	{
		_transitionChannels.Clear();
		_animationChannels.Clear();
		_hasComputedStyle = false;
		Style = LocalStyle;
		RenderStyle = LocalStyle;
	}

	internal void SetLayout(UiRect rect)
	{
		LayoutRect = rect;
	}

	internal void SetScrollMetrics(float contentWidth, float contentHeight)
	{
		ScrollContentWidth = Math.Max(LayoutRect.Width, contentWidth);
		ScrollContentHeight = Math.Max(LayoutRect.Height, contentHeight);
		SetScrollOffset(ScrollOffsetX, ScrollOffsetY);
	}

	internal bool SetScrollOffset(float offsetX, float offsetY)
	{
		float num = Math.Clamp(offsetX, 0f, MaxScrollX);
		float num2 = Math.Clamp(offsetY, 0f, MaxScrollY);
		if (Math.Abs(num - ScrollOffsetX) < 0.01f && Math.Abs(num2 - ScrollOffsetY) < 0.01f)
		{
			return false;
		}
		ScrollOffsetX = num;
		ScrollOffsetY = num2;
		return true;
	}

	internal bool ScrollBy(float deltaX, float deltaY)
	{
		return SetScrollOffset(ScrollOffsetX + deltaX, ScrollOffsetY + deltaY);
	}

	internal void SetPseudoState(UiPseudoState state)
	{
		PseudoState = state;
	}

	internal void AddPseudoState(UiPseudoState state)
	{
		PseudoState |= state;
	}

	public void AddActionHandle(UiActionHandle handle)
	{
		if (handle.IsValid)
		{
			_actionHandles.Add(handle);
		}
	}

	internal void RemovePseudoState(UiPseudoState state)
	{
		PseudoState &= (UiPseudoState)(ushort)(~(int)state);
	}

	public void SetCanvasContent(UiCanvasContent? canvasContent)
	{
		CanvasContent = canvasContent;
	}

	private bool HasEquivalentDefinition(UiNode other)
	{
		return string.Equals(TagName, other.TagName, StringComparison.Ordinal) &&
			string.Equals(ElementId, other.ElementId, StringComparison.Ordinal) &&
			object.Equals(LocalStyle, other.LocalStyle) &&
			string.Equals(TextContent, other.TextContent, StringComparison.Ordinal) &&
			object.ReferenceEquals(CanvasContent, other.CanvasContent) &&
			SequenceEqual(_classNames, other._classNames) &&
			BagsEqual(Attributes, other.Attributes) &&
			DeclarationsEqual(InlineStyle, other.InlineStyle);
	}

	private static bool SequenceEqual(IReadOnlyList<string> left, IReadOnlyList<string> right)
	{
		if (left.Count != right.Count)
		{
			return false;
		}

		for (int i = 0; i < left.Count; i++)
		{
			if (!string.Equals(left[i], right[i], StringComparison.Ordinal))
			{
				return false;
			}
		}

		return true;
	}

	private static bool BagsEqual(UiAttributeBag left, UiAttributeBag right)
	{
		if (left.Count != right.Count)
		{
			return false;
		}

		foreach (KeyValuePair<string, string> item in left)
		{
			if (!right.TryGetValue(item.Key, out string? value) || !string.Equals(value, item.Value, StringComparison.Ordinal))
			{
				return false;
			}
		}

		return true;
	}

	private static bool DeclarationsEqual(UiStyleDeclaration left, UiStyleDeclaration right)
	{
		if (left.Count != right.Count)
		{
			return false;
		}

		foreach (KeyValuePair<string, string> item in left)
		{
			if (!string.Equals(right[item.Key], item.Value, StringComparison.Ordinal))
			{
				return false;
			}
		}

		return true;
	}

	private static string GetDefaultTagName(UiNodeKind kind)
	{
		if (1 == 0)
		{
		}
		string result = kind switch
		{
			UiNodeKind.Text => "span", 
			UiNodeKind.Button => "button", 
			UiNodeKind.Image => "img", 
			UiNodeKind.Input => "input", 
			UiNodeKind.Checkbox => "input", 
			UiNodeKind.Radio => "input", 
			UiNodeKind.Toggle => "input", 
			UiNodeKind.Slider => "input", 
			UiNodeKind.Select => "select", 
			UiNodeKind.TextArea => "textarea", 
			UiNodeKind.Row => "div", 
			UiNodeKind.Column => "div", 
			UiNodeKind.Panel => "section", 
			UiNodeKind.Card => "article", 
			UiNodeKind.Table => "table", 
			UiNodeKind.TableHeader => "thead", 
			UiNodeKind.TableBody => "tbody", 
			UiNodeKind.TableFooter => "tfoot", 
			UiNodeKind.TableRow => "tr", 
			UiNodeKind.TableCell => "td", 
			UiNodeKind.TableHeaderCell => "th", 
			_ => "div", 
		};
		if (1 == 0)
		{
		}
		return result;
	}

	private static string[] SplitClasses(string? classText)
	{
		if (string.IsNullOrWhiteSpace(classText))
		{
			return Array.Empty<string>();
		}
		return classText.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct<string>(StringComparer.OrdinalIgnoreCase).ToArray();
	}

	private static bool HasMeaningfulStyleChange(UiStyle previous, UiStyle current)
	{
		return !object.Equals(previous with
		{
			Transition = null
		}, current with
		{
			Transition = null
		});
	}

	private void RestartAnimations(UiStyle style, UiAnimationSpec? animation)
	{
		_animationChannels.Clear();
		if (animation == null || animation.Entries.Count == 0)
		{
			return;
		}
		for (int i = 0; i < animation.Entries.Count; i++)
		{
			UiAnimationChannelState uiAnimationChannelState = new UiAnimationChannelState(animation.Entries[i], style);
			if (uiAnimationChannelState.HasTracks)
			{
				_animationChannels.Add(uiAnimationChannelState);
			}
		}
	}

	private UiStyle ComposeRenderStyle()
	{
		UiStyle uiStyle = Style;
		for (int i = 0; i < _transitionChannels.Count; i++)
		{
			uiStyle = UiTransitionMath.Apply(uiStyle, _transitionChannels[i]);
		}
		for (int j = 0; j < _animationChannels.Count; j++)
		{
			uiStyle = _animationChannels[j].Apply(uiStyle);
		}
		return uiStyle;
	}

	private void BeginVisualTransitions(UiStyle previousTarget, UiStyle targetStyle)
	{
		UiTransitionSpec uiTransitionSpec = targetStyle.Transition ?? previousTarget.Transition;
		if (uiTransitionSpec == null)
		{
			_transitionChannels.Clear();
			return;
		}
		List<UiTransitionChannelState> list = new List<UiTransitionChannelState>();
		UiStyle renderStyle = targetStyle;
		QueueColorTransition(uiTransitionSpec, "background-color", RenderStyle.BackgroundColor, targetStyle.BackgroundColor, ref renderStyle, list);
		QueueColorTransition(uiTransitionSpec, "border-color", RenderStyle.BorderColor, targetStyle.BorderColor, ref renderStyle, list);
		QueueColorTransition(uiTransitionSpec, "outline-color", RenderStyle.OutlineColor, targetStyle.OutlineColor, ref renderStyle, list);
		QueueColorTransition(uiTransitionSpec, "color", RenderStyle.Color, targetStyle.Color, ref renderStyle, list);
		QueueFloatTransition(uiTransitionSpec, "opacity", RenderStyle.Opacity, targetStyle.Opacity, ref renderStyle, list);
		QueueFloatTransition(uiTransitionSpec, "filter", RenderStyle.FilterBlurRadius, targetStyle.FilterBlurRadius, ref renderStyle, list);
		QueueFloatTransition(uiTransitionSpec, "backdrop-filter", RenderStyle.BackdropBlurRadius, targetStyle.BackdropBlurRadius, ref renderStyle, list);
		_transitionChannels.Clear();
		_transitionChannels.AddRange(list);
	}

	private static void QueueFloatTransition(UiTransitionSpec transition, string propertyName, float startValue, float endValue, ref UiStyle renderStyle, ICollection<UiTransitionChannelState> channels)
	{
		if (!(Math.Abs(startValue - endValue) < 0.001f) && transition.TryGet(propertyName, out UiTransitionEntry entry) && !(entry == null) && !(entry.DurationSeconds <= 0f))
		{
			channels.Add(new UiTransitionChannelState(propertyName, entry.DurationSeconds, entry.DelaySeconds, entry.Easing, startValue, endValue));
			renderStyle = UiTransitionMath.ApplyFloat(renderStyle, propertyName, startValue);
		}
	}

	private static void QueueColorTransition(UiTransitionSpec transition, string propertyName, UiColor startValue, UiColor endValue, ref UiStyle renderStyle, ICollection<UiTransitionChannelState> channels)
	{
		if (!(startValue == endValue) && transition.TryGet(propertyName, out UiTransitionEntry entry) && !(entry == null) && !(entry.DurationSeconds <= 0f))
		{
			channels.Add(new UiTransitionChannelState(propertyName, entry.DurationSeconds, entry.DelaySeconds, entry.Easing, startValue, endValue));
			renderStyle = UiTransitionMath.ApplyColor(renderStyle, propertyName, startValue);
		}
	}
}
