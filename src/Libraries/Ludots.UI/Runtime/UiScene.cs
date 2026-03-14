using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text.RegularExpressions;
using Ludots.UI.Runtime.Actions;
using Ludots.UI.Runtime.Diff;
using Ludots.UI.Runtime.Events;

namespace Ludots.UI.Runtime;

public sealed class UiScene
{
	private static readonly Regex BasicEmailPattern = new Regex("^[^@\\s]+@[^@\\s]+\\.[^@\\s]+$", RegexOptions.Compiled | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50.0));

	private readonly UiStyleResolver _styleResolver = new UiStyleResolver();

	private readonly UiLayoutEngine _layoutEngine = new UiLayoutEngine();

	private readonly List<UiStyleSheet> _styleSheets = new List<UiStyleSheet>();

	private readonly Dictionary<string, UiVirtualWindow> _virtualWindows = new Dictionary<string, UiVirtualWindow>(StringComparer.Ordinal);

	private Func<bool>? _reactiveRuntimeRefresh;

	private UiNodeId? _hoveredNodeId;

	private UiNodeId? _pressedNodeId;

	private UiNodeId? _focusedNodeId;

	private UiNodeId? _scrollDragNodeId;

	private UiScrollAxis _scrollDragAxis;

	private float _scrollDragPointerX;

	private float _scrollDragPointerY;

	private float _scrollDragStartOffsetX;

	private float _scrollDragStartOffsetY;

	private float _layoutWidth;

	private float _layoutHeight;

	private int _nextNodeId = 1;

	public UiDispatcher Dispatcher { get; }

	public UiNode? Root { get; private set; }

	public UiDocument? Document { get; private set; }

	public UiThemePack? Theme { get; private set; }

	public UiNodeId? FocusedNodeId => _focusedNodeId;

	public long Version { get; private set; }

	public bool IsDirty { get; private set; }

	public UiReactiveUpdateMetrics LastReactiveUpdateMetrics { get; internal set; } = UiReactiveUpdateMetrics.None;

	public UiScene(UiDispatcher? dispatcher = null)
	{
		Dispatcher = dispatcher ?? new UiDispatcher();
	}

	public void Mount(UiNode root)
	{
		Root = root ?? throw new ArgumentNullException("root");
		Document = null;
		TrackNextNodeId(root);
		ResetInteractionState();
		InitializeRuntimeState(root);
		Version++;
		IsDirty = true;
	}

	public void MountDocument(UiDocument document, UiThemePack? theme = null)
	{
		ArgumentNullException.ThrowIfNull(document, "document");
		Document = document;
		Theme = theme;
		_styleSheets.Clear();
		_styleSheets.AddRange(document.StyleSheets);
		_nextNodeId = 1;
		Root = BuildNode(document.Root);
		TrackNextNodeId(Root);
		ResetInteractionState();
		InitializeRuntimeState(Root);
		Version++;
		IsDirty = true;
	}

	public void SetTheme(UiThemePack? theme)
	{
		Theme = theme;
		Version++;
		IsDirty = true;
	}

	public void SetStyleSheets(params UiStyleSheet[] styleSheets)
	{
		_styleSheets.Clear();
		if (styleSheets != null && styleSheets.Length != 0)
		{
			_styleSheets.AddRange(styleSheets);
		}
		Version++;
		IsDirty = true;
	}

	public void Layout(float width, float height)
	{
		if (Root != null && (IsDirty || !(Math.Abs(_layoutWidth - width) < 0.01f) || !(Math.Abs(_layoutHeight - height) < 0.01f)))
		{
			_layoutWidth = width;
			_layoutHeight = height;
			_styleResolver.ResolveTree(Root, GetEffectiveStyleSheets());
			_layoutEngine.Layout(Root, width, height);
			IsDirty = false;
		}
	}

	public bool AdvanceTime(float deltaSeconds)
	{
		if (Root == null || deltaSeconds <= 0f)
		{
			return false;
		}
		bool flag = false;
		foreach (UiNode item in EnumerateNodes(Root))
		{
			flag |= item.AdvanceTransitions(deltaSeconds);
		}
		if (flag)
		{
			Version++;
		}
		return flag;
	}

	public UiEventResult Dispatch(UiEvent evt)
	{
		ArgumentNullException.ThrowIfNull(evt, "evt");
		if (Root == null)
		{
			return UiEventResult.Unhandled;
		}
		UiNode uiNode = ResolveTarget(evt);
		bool flag = false;
		if (evt is UiPointerEvent uiPointerEvent)
		{
			(bool Consumed, bool Changed) tuple = HandleScrollInteraction(uiPointerEvent, uiNode);
			bool item = tuple.Consumed;
			bool item2 = tuple.Changed;
			flag = flag || item2;
			if (item || uiPointerEvent.PointerEventType == UiPointerEventType.Scroll)
			{
				if (flag)
				{
					Version++;
					IsDirty = true;
				}
				return (item || flag) ? UiEventResult.CreateHandled() : UiEventResult.Unhandled;
			}
			flag |= UpdatePointerState(uiPointerEvent, uiNode);
			flag |= UpdateFocusState(uiPointerEvent, uiNode);
			if (uiPointerEvent.PointerEventType != UiPointerEventType.Click)
			{
				if (flag)
				{
					Version++;
					IsDirty = true;
				}
				return (uiNode != null || flag) ? UiEventResult.CreateHandled() : UiEventResult.Unhandled;
			}
			flag |= UpdateSemanticState(uiNode);
		}
		bool flag2 = false;
		for (UiNode uiNode2 = uiNode; uiNode2 != null; uiNode2 = uiNode2.Parent)
		{
			if (DispatchNodeActions(uiNode2, evt))
			{
				flag2 = true;
				break;
			}
		}
		if (flag2 || flag)
		{
			Version++;
			IsDirty = true;
		}
		return (flag2 || flag) ? UiEventResult.CreateHandled() : UiEventResult.Unhandled;
	}

	public UiSceneDiff CreateFullDiff()
	{
		UiSceneSnapshot snapshot = new UiSceneSnapshot(Version, (Root != null) ? UiNodeDiff.FromNode(Root) : null);
		IsDirty = false;
		return new UiSceneDiff(UiSceneDiffKind.FullSnapshot, snapshot);
	}

	public bool TryGetVirtualWindow(string hostElementId, out UiVirtualWindow window)
	{
		return _virtualWindows.TryGetValue(hostElementId, out window);
	}

	public UiNode? FindNode(UiNodeId id)
	{
		return (Root == null) ? null : FindNode(Root, id);
	}

	public UiNode? FindByElementId(string elementId)
	{
		return (Root == null) ? null : FindByElementId(Root, elementId);
	}

	public UiNode? QuerySelector(string selectorText)
	{
		return QuerySelectorAll(selectorText).FirstOrDefault();
	}

	public IReadOnlyList<UiNode> QuerySelectorAll(string selectorText)
	{
		if (Root == null)
		{
			return Array.Empty<UiNode>();
		}
		IReadOnlyList<UiSelector> selectors = UiSelectorParser.ParseMany(selectorText);
		List<UiNode> list = new List<UiNode>();
		Traverse(Root, selectors, list);
		return list;
	}

	public UiNode? HitTest(float x, float y)
	{
		return (Root == null) ? null : HitTest(Root, x, y);
	}

	internal void SetReactiveRuntimeRefresh(Func<bool>? runtimeRefresh)
	{
		_reactiveRuntimeRefresh = runtimeRefresh;
	}

	internal bool TryRefreshReactiveRuntimeDependencies()
	{
		return _reactiveRuntimeRefresh != null && _reactiveRuntimeRefresh();
	}

	private UiNode BuildNode(UiElement element)
	{
		UiAttributeBag uiAttributeBag = new UiAttributeBag();
		foreach (KeyValuePair<string, string> attribute in element.Attributes)
		{
			uiAttributeBag[attribute.Key] = attribute.Value;
		}
		string elementId = uiAttributeBag["id"];
		IReadOnlyList<string> classList = uiAttributeBag.GetClassList();
		UiNode[] children = element.Children.Select(BuildNode).ToArray();
		return new UiNode(new UiNodeId(_nextNodeId++), element.Kind, null, element.TextContent, children, null, element.TagName, elementId, classList, uiAttributeBag, element.InlineStyle);
	}

	internal int GetNextReactiveNodeIdSeed()
	{
		if (Root == null)
		{
			return Math.Max(1, _nextNodeId);
		}

		int nextSeed = GetMaxNodeId(Root) + 1;
		if (nextSeed > _nextNodeId)
		{
			_nextNodeId = nextSeed;
		}

		return _nextNodeId;
	}

	internal UiRetainedPatchStats ApplyReactiveRoot(UiNode root)
	{
		ArgumentNullException.ThrowIfNull(root, "root");
		if (Root == null)
		{
			Mount(root);
			return new UiRetainedPatchStats(0, 0, UiRetainedTreeReconciler.CountSubtree(root), 0, 1, true);
		}

		if (!UiRetainedTreeReconciler.CanReuseNode(Root, root))
		{
			int removedNodes = UiRetainedTreeReconciler.CountSubtree(Root);
			Mount(root);
			return new UiRetainedPatchStats(0, 0, UiRetainedTreeReconciler.CountSubtree(root), removedNodes, 1, true);
		}

		UiRetainedPatchStats stats = UiRetainedTreeReconciler.Reconcile(Root, root);
		TrackNextNodeId(Root);
		if (stats.HasChanges)
		{
			RefreshRetainedRuntimeState(Root);
			ClearStaleInteractionState();
			Version++;
			IsDirty = true;
		}

		return stats;
	}

	internal void SetVirtualWindows(IReadOnlyDictionary<string, UiVirtualWindow> windows)
	{
		_virtualWindows.Clear();
		foreach (KeyValuePair<string, UiVirtualWindow> item in windows)
		{
			_virtualWindows[item.Key] = item.Value;
		}
	}

	private static bool IsTruthy(string? value)
	{
		return !string.IsNullOrWhiteSpace(value) && (value.Equals("true", StringComparison.OrdinalIgnoreCase) || value.Equals("checked", StringComparison.OrdinalIgnoreCase) || value.Equals("selected", StringComparison.OrdinalIgnoreCase) || value.Equals("1", StringComparison.OrdinalIgnoreCase));
	}

	private void ResetInteractionState()
	{
		_hoveredNodeId = null;
		_pressedNodeId = null;
		_focusedNodeId = null;
		ClearScrollDrag();
	}

	private void InitializeRuntimeState(UiNode node)
	{
		node.ResetVisualState();
		node.RemovePseudoState(UiPseudoState.Hover | UiPseudoState.Active | UiPseudoState.Focus | UiPseudoState.Disabled | UiPseudoState.Checked | UiPseudoState.Selected | UiPseudoState.Required | UiPseudoState.Invalid);
		if (HasBooleanAttribute(node.Attributes, "disabled"))
		{
			node.AddPseudoState(UiPseudoState.Disabled);
		}
		if (HasBooleanAttribute(node.Attributes, "checked"))
		{
			node.AddPseudoState(UiPseudoState.Checked);
		}
		if (HasBooleanAttribute(node.Attributes, "selected") || HasBooleanAttribute(node.Attributes, "aria-selected"))
		{
			node.AddPseudoState(UiPseudoState.Selected);
		}
		RefreshValidationState(node);
		foreach (UiNode child in node.Children)
		{
			InitializeRuntimeState(child);
		}
	}

	private void RefreshRetainedRuntimeState(UiNode node)
	{
		node.RemovePseudoState(UiPseudoState.Disabled | UiPseudoState.Checked | UiPseudoState.Selected | UiPseudoState.Required | UiPseudoState.Invalid);
		if (HasBooleanAttribute(node.Attributes, "disabled"))
		{
			node.AddPseudoState(UiPseudoState.Disabled);
		}
		if (HasBooleanAttribute(node.Attributes, "checked"))
		{
			node.AddPseudoState(UiPseudoState.Checked);
		}
		if (HasBooleanAttribute(node.Attributes, "selected") || HasBooleanAttribute(node.Attributes, "aria-selected"))
		{
			node.AddPseudoState(UiPseudoState.Selected);
		}
		RefreshValidationState(node);
		foreach (UiNode child in node.Children)
		{
			RefreshRetainedRuntimeState(child);
		}
	}

	private static bool HasBooleanAttribute(UiAttributeBag attributes, string name)
	{
		return attributes.Contains(name) || IsTruthy(attributes[name]);
	}

	private void ClearStaleInteractionState()
	{
		if (!HasLiveNode(_hoveredNodeId))
		{
			_hoveredNodeId = null;
		}

		if (!HasLiveNode(_pressedNodeId))
		{
			_pressedNodeId = null;
		}

		if (!HasLiveNode(_focusedNodeId))
		{
			_focusedNodeId = null;
		}

		if (!HasLiveNode(_scrollDragNodeId))
		{
			ClearScrollDrag();
		}
	}

	private bool HasLiveNode(UiNodeId? nodeId)
	{
		if (!nodeId.HasValue)
		{
			return false;
		}

		UiNodeId valueOrDefault = nodeId.GetValueOrDefault();
		return valueOrDefault.IsValid && FindNode(valueOrDefault) != null;
	}

	private void TrackNextNodeId(UiNode? node)
	{
		if (node == null)
		{
			return;
		}

		int nextSeed = GetMaxNodeId(node) + 1;
		if (nextSeed > _nextNodeId)
		{
			_nextNodeId = nextSeed;
		}
	}

	private static int GetMaxNodeId(UiNode node)
	{
		int max = node.Id.Value;
		for (int i = 0; i < node.Children.Count; i++)
		{
			int childMax = GetMaxNodeId(node.Children[i]);
			if (childMax > max)
			{
				max = childMax;
			}
		}

		return max;
	}

	private IReadOnlyList<UiStyleSheet> GetEffectiveStyleSheets()
	{
		List<UiStyleSheet> list = new List<UiStyleSheet>(_styleSheets.Count + (Theme?.StyleSheets.Count ?? 0));
		list.AddRange(_styleSheets);
		if (Theme != null)
		{
			list.AddRange(Theme.StyleSheets);
		}
		return list;
	}

	private UiNode? ResolveTarget(UiEvent evt)
	{
		UiNodeId? targetNodeId = evt.TargetNodeId;
		if (targetNodeId.HasValue)
		{
			UiNodeId valueOrDefault = targetNodeId.GetValueOrDefault();
			if (valueOrDefault.IsValid)
			{
				return FindNode(valueOrDefault);
			}
		}
		if (evt is UiPointerEvent uiPointerEvent)
		{
			return HitTest(uiPointerEvent.X, uiPointerEvent.Y);
		}
		return null;
	}

	private bool DispatchNodeActions(UiNode node, UiEvent evt)
	{
		for (int i = 0; i < node.ActionHandles.Count; i++)
		{
			UiActionHandle handle = node.ActionHandles[i];
			if (Dispatcher.Dispatch(handle, new UiActionContext(this, evt, node)))
			{
				return true;
			}
		}
		return false;
	}

	private (bool Consumed, bool Changed) HandleScrollInteraction(UiPointerEvent evt, UiNode? targetNode)
	{
		UiNodeId? scrollDragNodeId = _scrollDragNodeId;
		if (scrollDragNodeId.HasValue)
		{
			UiNodeId valueOrDefault = scrollDragNodeId.GetValueOrDefault();
			if (valueOrDefault.IsValid)
			{
				UiNode uiNode = FindNode(valueOrDefault);
				if (uiNode == null)
				{
					ClearScrollDrag();
					return (Consumed: false, Changed: false);
				}
				UiPointerEventType pointerEventType = evt.PointerEventType;
				if (1 == 0)
				{
				}
				(bool, bool) result = pointerEventType switch
				{
					UiPointerEventType.Move => (true, UpdateScrollDrag(uiNode, evt.X, evt.Y)), 
					UiPointerEventType.Up => (true, ClearActiveScrollDrag()), 
					_ => (true, false), 
				};
				if (1 == 0)
				{
				}
				return result;
			}
		}
		UiNode uiNode2 = ResolveScrollContainer(targetNode);
		if (uiNode2 == null)
		{
			return (Consumed: false, Changed: false);
		}
		if (evt.PointerEventType == UiPointerEventType.Scroll)
		{
			bool flag = uiNode2.ScrollBy(evt.DeltaX, evt.DeltaY);
			return (Consumed: flag, Changed: flag);
		}
		if (evt.PointerEventType == UiPointerEventType.Down && TryStartScrollDrag(uiNode2, evt.X, evt.Y))
		{
			return (Consumed: true, Changed: false);
		}
		if (evt.PointerEventType == UiPointerEventType.Up)
		{
			ClearScrollDrag();
		}
		return (Consumed: false, Changed: false);
	}

	private UiNode? ResolveScrollContainer(UiNode? node)
	{
		for (UiNode uiNode = node; uiNode != null; uiNode = uiNode.Parent)
		{
			if (uiNode.Style.Overflow == UiOverflow.Scroll)
			{
				return uiNode;
			}
		}
		return null;
	}

	private bool TryStartScrollDrag(UiNode node, float x, float y)
	{
		UiRect verticalThumbRect = UiScrollGeometry.GetVerticalThumbRect(node);
		if (verticalThumbRect.Width > 0f && verticalThumbRect.Height > 0f && verticalThumbRect.Contains(x, y))
		{
			_scrollDragNodeId = node.Id;
			_scrollDragAxis = UiScrollAxis.Vertical;
			_scrollDragPointerX = x;
			_scrollDragPointerY = y;
			_scrollDragStartOffsetX = node.ScrollOffsetX;
			_scrollDragStartOffsetY = node.ScrollOffsetY;
			return true;
		}
		UiRect horizontalThumbRect = UiScrollGeometry.GetHorizontalThumbRect(node);
		if (horizontalThumbRect.Width > 0f && horizontalThumbRect.Height > 0f && horizontalThumbRect.Contains(x, y))
		{
			_scrollDragNodeId = node.Id;
			_scrollDragAxis = UiScrollAxis.Horizontal;
			_scrollDragPointerX = x;
			_scrollDragPointerY = y;
			_scrollDragStartOffsetX = node.ScrollOffsetX;
			_scrollDragStartOffsetY = node.ScrollOffsetY;
			return true;
		}
		return false;
	}

	private bool UpdateScrollDrag(UiNode node, float pointerX, float pointerY)
	{
		switch (_scrollDragAxis)
		{
		case UiScrollAxis.Vertical:
		{
			UiRect verticalTrackRect = UiScrollGeometry.GetVerticalTrackRect(node);
			UiRect verticalThumbRect = UiScrollGeometry.GetVerticalThumbRect(node);
			float num3 = Math.Max(0f, verticalTrackRect.Height - verticalThumbRect.Height);
			if (num3 <= 0.01f || node.MaxScrollY <= 0.01f)
			{
				return false;
			}
			float num4 = pointerY - _scrollDragPointerY;
			float offsetY = _scrollDragStartOffsetY + num4 / num3 * node.MaxScrollY;
			return node.SetScrollOffset(node.ScrollOffsetX, offsetY);
		}
		case UiScrollAxis.Horizontal:
		{
			UiRect horizontalTrackRect = UiScrollGeometry.GetHorizontalTrackRect(node);
			UiRect horizontalThumbRect = UiScrollGeometry.GetHorizontalThumbRect(node);
			float num = Math.Max(0f, horizontalTrackRect.Width - horizontalThumbRect.Width);
			if (num <= 0.01f || node.MaxScrollX <= 0.01f)
			{
				return false;
			}
			float num2 = pointerX - _scrollDragPointerX;
			float offsetX = _scrollDragStartOffsetX + num2 / num * node.MaxScrollX;
			return node.SetScrollOffset(offsetX, node.ScrollOffsetY);
		}
		default:
			return false;
		}
	}

	private void ClearScrollDrag()
	{
		_scrollDragNodeId = null;
		_scrollDragAxis = UiScrollAxis.None;
		_scrollDragPointerX = 0f;
		_scrollDragPointerY = 0f;
		_scrollDragStartOffsetX = 0f;
		_scrollDragStartOffsetY = 0f;
	}

	private bool ClearActiveScrollDrag()
	{
		bool result = _scrollDragNodeId?.IsValid ?? false;
		ClearScrollDrag();
		return result;
	}

	private bool UpdatePointerState(UiPointerEvent evt, UiNode? targetNode)
	{
		bool result = false;
		UiNodeId? hoveredNodeId = _hoveredNodeId;
		if (hoveredNodeId.HasValue)
		{
			UiNodeId valueOrDefault = hoveredNodeId.GetValueOrDefault();
			if (valueOrDefault.IsValid)
			{
				UiNodeId value = valueOrDefault;
				UiNodeId? obj = targetNode?.Id;
				if (value != obj)
				{
					FindNode(valueOrDefault)?.RemovePseudoState(UiPseudoState.Hover);
					result = true;
				}
			}
		}
		if (targetNode != null)
		{
			if (_hoveredNodeId != targetNode.Id)
			{
				targetNode.AddPseudoState(UiPseudoState.Hover);
				result = true;
			}
			_hoveredNodeId = targetNode.Id;
		}
		else if (_hoveredNodeId.HasValue)
		{
			_hoveredNodeId = null;
			result = true;
		}
		if (evt.PointerEventType == UiPointerEventType.Down && targetNode != null)
		{
			hoveredNodeId = _pressedNodeId;
			if (hoveredNodeId.HasValue)
			{
				UiNodeId valueOrDefault2 = hoveredNodeId.GetValueOrDefault();
				if (valueOrDefault2.IsValid && valueOrDefault2 != targetNode.Id)
				{
					FindNode(valueOrDefault2)?.RemovePseudoState(UiPseudoState.Active);
				}
			}
			_pressedNodeId = targetNode.Id;
			targetNode.AddPseudoState(UiPseudoState.Active);
			result = true;
		}
		else if (evt.PointerEventType == UiPointerEventType.Up)
		{
			hoveredNodeId = _pressedNodeId;
			if (hoveredNodeId.HasValue)
			{
				UiNodeId valueOrDefault3 = hoveredNodeId.GetValueOrDefault();
				if (valueOrDefault3.IsValid)
				{
					FindNode(valueOrDefault3)?.RemovePseudoState(UiPseudoState.Active);
					result = true;
				}
			}
			_pressedNodeId = null;
		}
		return result;
	}

	private bool UpdateFocusState(UiPointerEvent evt, UiNode? targetNode)
	{
		UiPointerEventType pointerEventType = evt.PointerEventType;
		if (1 == 0)
		{
		}
		bool result = (pointerEventType == UiPointerEventType.Down || pointerEventType == UiPointerEventType.Click) && SetFocusedNode(ResolveFocusableNode(targetNode));
		if (1 == 0)
		{
		}
		return result;
	}

	private bool UpdateSemanticState(UiNode? targetNode)
	{
		UiNode uiNode = ResolveSemanticNode(targetNode);
		if (uiNode == null || uiNode.PseudoState.HasFlag(UiPseudoState.Disabled))
		{
			return false;
		}
		if (IsRadioNode(uiNode))
		{
			bool flag = false;
			string text = uiNode.Attributes["name"];
			if (!string.IsNullOrWhiteSpace(text) && Root != null)
			{
				foreach (UiNode item in EnumerateNodes(Root))
				{
					if (item != uiNode && IsRadioNode(item) && string.Equals(item.Attributes["name"], text, StringComparison.OrdinalIgnoreCase))
					{
						flag |= SetCheckedState(item, value: false);
					}
				}
			}
			flag |= SetCheckedState(uiNode, value: true);
			return string.IsNullOrWhiteSpace(text) ? (flag | RefreshValidationState(uiNode)) : (flag | RefreshRadioGroupValidation(text));
		}
		if (IsCheckableNode(uiNode))
		{
			bool flag2 = uiNode.PseudoState.HasFlag(UiPseudoState.Checked);
			bool flag3 = SetCheckedState(uiNode, !flag2);
			return flag3 | RefreshValidationState(uiNode);
		}
		return false;
	}

	private bool SetFocusedNode(UiNode? node)
	{
		UiNodeId? uiNodeId = node?.Id;
		if (_focusedNodeId == uiNodeId)
		{
			return false;
		}
		UiNodeId? focusedNodeId = _focusedNodeId;
		if (focusedNodeId.HasValue)
		{
			UiNodeId valueOrDefault = focusedNodeId.GetValueOrDefault();
			if (valueOrDefault.IsValid)
			{
				FindNode(valueOrDefault)?.RemovePseudoState(UiPseudoState.Focus);
			}
		}
		_focusedNodeId = uiNodeId;
		node?.AddPseudoState(UiPseudoState.Focus);
		return true;
	}

	private UiNode? ResolveFocusableNode(UiNode? node)
	{
		for (UiNode uiNode = node; uiNode != null; uiNode = uiNode.Parent)
		{
			if (IsFocusableNode(uiNode))
			{
				return uiNode;
			}
		}
		return null;
	}

	private UiNode? ResolveSemanticNode(UiNode? node)
	{
		for (UiNode uiNode = node; uiNode != null; uiNode = uiNode.Parent)
		{
			if (IsCheckableNode(uiNode) || IsRadioNode(uiNode))
			{
				return uiNode;
			}
		}
		return null;
	}

	private bool RefreshRadioGroupValidation(string groupName)
	{
		if (string.IsNullOrWhiteSpace(groupName) || Root == null)
		{
			return false;
		}
		bool flag = false;
		foreach (UiNode item in EnumerateNodes(Root))
		{
			if (IsRadioNode(item) && string.Equals(item.Attributes["name"], groupName, StringComparison.OrdinalIgnoreCase))
			{
				flag |= RefreshValidationState(item);
			}
		}
		return flag;
	}

	private bool RefreshValidationState(UiNode node)
	{
		bool flag = false;
		bool flag2 = IsRequiredNode(node);
		flag |= SetPseudoFlag(node, UiPseudoState.Required, flag2);
		bool flag3 = EvaluateInvalidState(node, flag2);
		flag |= SetPseudoFlag(node, UiPseudoState.Invalid, flag3);
		if (flag2)
		{
			node.Attributes["aria-required"] = "true";
		}
		if (flag3)
		{
			node.Attributes["aria-invalid"] = "true";
		}
		else if (IsConstraintValidatedNode(node) || IsCheckableNode(node) || IsRadioNode(node) || node.Attributes.Contains("aria-invalid"))
		{
			node.Attributes["aria-invalid"] = "false";
		}
		return flag;
	}

	private bool EvaluateInvalidState(UiNode node, bool required)
	{
		if (node.PseudoState.HasFlag(UiPseudoState.Disabled))
		{
			return false;
		}
		if (IsRadioNode(node))
		{
			if (!required)
			{
				return false;
			}
			string text = node.Attributes["name"];
			if (!string.IsNullOrWhiteSpace(text) && Root != null)
			{
				foreach (UiNode item in EnumerateNodes(Root))
				{
					if (IsRadioNode(item) && string.Equals(item.Attributes["name"], text, StringComparison.OrdinalIgnoreCase) && item.PseudoState.HasFlag(UiPseudoState.Checked))
					{
						return false;
					}
				}
				return true;
			}
			return !node.PseudoState.HasFlag(UiPseudoState.Checked);
		}
		if (IsCheckableNode(node))
		{
			return required && !node.PseudoState.HasFlag(UiPseudoState.Checked);
		}
		if (!IsConstraintValidatedNode(node))
		{
			return false;
		}
		string value = ResolveConstraintValue(node);
		bool flag = string.IsNullOrWhiteSpace(value);
		if (required && flag)
		{
			return true;
		}
		if (flag)
		{
			return false;
		}
		return ViolatesInputConstraints(node, value);
	}

	private bool IsRequiredNode(UiNode node)
	{
		if (node.PseudoState.HasFlag(UiPseudoState.Disabled))
		{
			return false;
		}
		if (node.Attributes.Contains("required") || IsTruthy(node.Attributes["aria-required"]))
		{
			return true;
		}
		if (IsRadioNode(node) && Root != null)
		{
			string text = node.Attributes["name"];
			if (!string.IsNullOrWhiteSpace(text))
			{
				foreach (UiNode item in EnumerateNodes(Root))
				{
					if (IsRadioNode(item) && string.Equals(item.Attributes["name"], text, StringComparison.OrdinalIgnoreCase) && (item.Attributes.Contains("required") || IsTruthy(item.Attributes["aria-required"])))
					{
						return true;
					}
				}
			}
		}
		return false;
	}

	private static bool SetPseudoFlag(UiNode node, UiPseudoState flag, bool value)
	{
		bool flag2 = node.PseudoState.HasFlag(flag);
		if (flag2 == value)
		{
			return false;
		}
		if (value)
		{
			node.AddPseudoState(flag);
		}
		else
		{
			node.RemovePseudoState(flag);
		}
		return true;
	}

	private static bool IsConstraintValidatedNode(UiNode node)
	{
		UiNodeKind kind = node.Kind;
		bool flag = ((kind == UiNodeKind.Input || kind - 11 <= UiNodeKind.Button) ? true : false);
		return flag || (string.Equals(node.TagName, "input", StringComparison.OrdinalIgnoreCase) && !IsCheckableNode(node) && !IsRadioNode(node) && !IsInputType(node, "button") && !IsInputType(node, "submit") && !IsInputType(node, "reset"));
	}

	private static string? ResolveConstraintValue(UiNode node)
	{
		string text = node.Attributes["value"];
		if (string.IsNullOrWhiteSpace(text) && node.Kind == UiNodeKind.TextArea)
		{
			text = node.TextContent;
		}
		return text;
	}

	private static bool ViolatesInputConstraints(UiNode node, string value)
	{
		if (ViolatesLengthConstraint(node, value))
		{
			return true;
		}
		if (ViolatesPatternConstraint(node, value))
		{
			return true;
		}
		string normalizedInputType = GetNormalizedInputType(node);
		if (1 == 0)
		{
		}
		bool result;
		switch (normalizedInputType)
		{
		case "email":
			result = !BasicEmailPattern.IsMatch(value);
			break;
		case "number":
		case "range":
			result = ViolatesNumericConstraint(node, value);
			break;
		case "url":
		{
			result = !Uri.TryCreate(value, UriKind.Absolute, out Uri _);
			break;
		}
		default:
			result = false;
			break;
		}
		if (1 == 0)
		{
		}
		return result;
	}

	private static bool ViolatesLengthConstraint(UiNode node, string value)
	{
		if (TryParseIntegerAttribute(node, "minlength", out var value2) && value.Length < value2)
		{
			return true;
		}
		int value3;
		return TryParseIntegerAttribute(node, "maxlength", out value3) && value.Length > value3;
	}

	private static bool ViolatesPatternConstraint(UiNode node, string value)
	{
		string text = node.Attributes["pattern"];
		if (string.IsNullOrWhiteSpace(text))
		{
			return false;
		}
		try
		{
			Regex regex = new Regex("^(?:" + text + ")$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50.0));
			return !regex.IsMatch(value);
		}
		catch (ArgumentException)
		{
			return false;
		}
	}

	private static bool ViolatesNumericConstraint(UiNode node, string value)
	{
		if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result))
		{
			return true;
		}
		if (TryParseFloatAttribute(node, "min", out var value2) && result < value2)
		{
			return true;
		}
		if (TryParseFloatAttribute(node, "max", out var value3) && result > value3)
		{
			return true;
		}
		string text = node.Attributes["step"];
		if (string.IsNullOrWhiteSpace(text) || string.Equals(text, "any", StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}
		if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var result2) || result2 <= 0.0)
		{
			return false;
		}
		double value4;
		double num = (TryParseFloatAttribute(node, "min", out value4) ? value4 : 0.0);
		double num2 = (result - num) / result2;
		double num3 = Math.Abs(num2 - Math.Round(num2));
		double num4 = Math.Max(1E-06, Math.Abs(result2) * 1E-06);
		return num3 > num4;
	}

	private static string GetNormalizedInputType(UiNode node)
	{
		if (node.Kind == UiNodeKind.Slider)
		{
			return "range";
		}
		return node.Attributes["type"]?.Trim().ToLowerInvariant() ?? string.Empty;
	}

	private static bool TryParseIntegerAttribute(UiNode node, string attributeName, out int value)
	{
		string s = node.Attributes[attributeName];
		return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
	}

	private static bool TryParseFloatAttribute(UiNode node, string attributeName, out double value)
	{
		string s = node.Attributes[attributeName];
		return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
	}

	private static bool IsFocusableNode(UiNode node)
	{
		if (node.PseudoState.HasFlag(UiPseudoState.Disabled))
		{
			return false;
		}
		if (node.Attributes.Contains("tabindex"))
		{
			return true;
		}
		if (node.ActionHandles.Count > 0 && node.Kind != UiNodeKind.Text)
		{
			return true;
		}
		UiNodeKind kind = node.Kind;
		return (kind == UiNodeKind.Button || kind - 7 <= UiNodeKind.Column) ? true : false;
	}

	private static bool IsCheckableNode(UiNode node)
	{
		UiNodeKind kind = node.Kind;
		bool flag = ((kind == UiNodeKind.Checkbox || kind == UiNodeKind.Toggle) ? true : false);
		return flag || IsInputType(node, "checkbox");
	}

	private static bool IsRadioNode(UiNode node)
	{
		return node.Kind == UiNodeKind.Radio || IsInputType(node, "radio");
	}

	private static bool IsInputType(UiNode node, string type)
	{
		return string.Equals(node.TagName, "input", StringComparison.OrdinalIgnoreCase) && string.Equals(node.Attributes["type"], type, StringComparison.OrdinalIgnoreCase);
	}

	private static bool SetCheckedState(UiNode node, bool value)
	{
		bool flag = node.PseudoState.HasFlag(UiPseudoState.Checked);
		if (flag == value)
		{
			return false;
		}
		if (value)
		{
			node.AddPseudoState(UiPseudoState.Checked);
			node.Attributes["checked"] = "true";
			node.Attributes["aria-checked"] = "true";
		}
		else
		{
			node.RemovePseudoState(UiPseudoState.Checked);
			node.Attributes["checked"] = null;
			node.Attributes["aria-checked"] = "false";
		}
		return true;
	}

	private static IEnumerable<UiNode> EnumerateNodes(UiNode root)
	{
		Stack<UiNode> stack = new Stack<UiNode>();
		stack.Push(root);
		while (stack.Count > 0)
		{
			UiNode current = stack.Pop();
			yield return current;
			for (int i = current.Children.Count - 1; i >= 0; i--)
			{
				stack.Push(current.Children[i]);
			}
		}
	}

	private static void Traverse(UiNode node, IReadOnlyList<UiSelector> selectors, List<UiNode> matches)
	{
		for (int i = 0; i < selectors.Count; i++)
		{
			if (UiSelectorMatcher.Matches(node, selectors[i]))
			{
				matches.Add(node);
				break;
			}
		}
		foreach (UiNode child in node.Children)
		{
			Traverse(child, selectors, matches);
		}
	}

	private static UiNode? FindNode(UiNode currentNode, UiNodeId targetNodeId)
	{
		if (currentNode.Id == targetNodeId)
		{
			return currentNode;
		}
		foreach (UiNode child in currentNode.Children)
		{
			UiNode uiNode = FindNode(child, targetNodeId);
			if (uiNode != null)
			{
				return uiNode;
			}
		}
		return null;
	}

	private static UiNode? FindByElementId(UiNode currentNode, string elementId)
	{
		if (string.Equals(currentNode.ElementId, elementId, StringComparison.OrdinalIgnoreCase))
		{
			return currentNode;
		}
		foreach (UiNode child in currentNode.Children)
		{
			UiNode uiNode = FindByElementId(child, elementId);
			if (uiNode != null)
			{
				return uiNode;
			}
		}
		return null;
	}

	private static UiNode? HitTest(UiNode node, float x, float y)
	{
		return HitTest(node, x, y, Matrix3x2.Identity);
	}

	private static UiNode? HitTest(UiNode node, float x, float y, Matrix3x2 accumulatedTransform)
	{
		UiStyle renderStyle = node.RenderStyle;
		if (!renderStyle.Visible || renderStyle.Display == UiDisplay.None)
		{
			return null;
		}
		Matrix3x2 current = renderStyle.Transform.HasOperations
			? accumulatedTransform * UiTransformMath.CreateMatrix(renderStyle, node.LayoutRect)
			: accumulatedTransform;
		if (!Matrix3x2.Invert(current, out var inverse))
		{
			return null;
		}
		Vector2 localPoint = Vector2.Transform(new Vector2(x, y), inverse);
		bool flag = node.LayoutRect.Contains(localPoint.X, localPoint.Y);
		bool flag2 = renderStyle.ClipContent || renderStyle.Overflow == UiOverflow.Scroll;
		if (!flag && flag2)
		{
			return null;
		}
		Matrix3x2 childTransform = renderStyle.Overflow == UiOverflow.Scroll
			? current * Matrix3x2.CreateTranslation(-node.ScrollOffsetX, -node.ScrollOffsetY)
			: current;
		foreach (UiNode item in UiVisualTreeOrdering.FrontToBack(node.Children))
		{
			UiNode uiNode = HitTest(item, x, y, childTransform);
			if (uiNode != null)
			{
				return uiNode;
			}
		}
		return flag ? node : null;
	}
}
