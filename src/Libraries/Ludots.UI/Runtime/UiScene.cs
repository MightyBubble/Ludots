using System.Globalization;
using System.Text.RegularExpressions;
using Ludots.UI.Runtime.Actions;
using Ludots.UI.Runtime.Diff;
using Ludots.UI.Runtime.Events;
using SkiaSharp;

namespace Ludots.UI.Runtime;

public sealed class UiScene
{
    private static readonly Regex BasicEmailPattern = new(
        "^[^@\\s]+@[^@\\s]+\\.[^@\\s]+$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(50));

    private readonly UiStyleResolver _styleResolver = new();
    private readonly UiLayoutEngine _layoutEngine = new();
    private readonly List<UiStyleSheet> _styleSheets = new();
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

    public UiScene(UiDispatcher? dispatcher = null)
    {
        Dispatcher = dispatcher ?? new UiDispatcher();
    }

    public UiDispatcher Dispatcher { get; }

    public UiNode? Root { get; private set; }

    public UiDocument? Document { get; private set; }

    public UiThemePack? Theme { get; private set; }

    public UiNodeId? FocusedNodeId => _focusedNodeId;

    public long Version { get; private set; }

    public bool IsDirty { get; private set; }

    public void Mount(UiNode root)
    {
        Root = root ?? throw new ArgumentNullException(nameof(root));
        Document = null;
        ResetInteractionState();
        InitializeRuntimeState(root);
        Version++;
        IsDirty = true;
    }

    public void MountDocument(UiDocument document, UiThemePack? theme = null)
    {
        ArgumentNullException.ThrowIfNull(document);

        Document = document;
        Theme = theme;
        _styleSheets.Clear();
        _styleSheets.AddRange(document.StyleSheets);
        _nextNodeId = 1;
        Root = BuildNode(document.Root);
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
        if (styleSheets != null && styleSheets.Length > 0)
        {
            _styleSheets.AddRange(styleSheets);
        }

        Version++;
        IsDirty = true;
    }

    public void Layout(float width, float height)
    {
        if (Root == null)
        {
            return;
        }

        if (!IsDirty && Math.Abs(_layoutWidth - width) < 0.01f && Math.Abs(_layoutHeight - height) < 0.01f)
        {
            return;
        }

        _layoutWidth = width;
        _layoutHeight = height;
        _styleResolver.ResolveTree(Root, GetEffectiveStyleSheets());
        _layoutEngine.Layout(Root, width, height);
        IsDirty = false;
    }

    public bool AdvanceTime(float deltaSeconds)
    {
        if (Root == null || deltaSeconds <= 0f)
        {
            return false;
        }

        bool changed = false;
        foreach (UiNode node in EnumerateNodes(Root))
        {
            changed |= node.AdvanceTransitions(deltaSeconds);
        }

        if (changed)
        {
            Version++;
        }

        return changed;
    }

    public UiEventResult Dispatch(UiEvent evt)
    {
        ArgumentNullException.ThrowIfNull(evt);

        if (Root == null)
        {
            return UiEventResult.Unhandled;
        }

        UiNode? targetNode = ResolveTarget(evt);
        bool sceneChanged = false;

        if (evt is UiPointerEvent pointerEvent)
        {
            (bool consumedByScroll, bool scrollChanged) = HandleScrollInteraction(pointerEvent, targetNode);
            sceneChanged |= scrollChanged;
            if (consumedByScroll || pointerEvent.PointerEventType == UiPointerEventType.Scroll)
            {
                if (sceneChanged)
                {
                    Version++;
                    IsDirty = true;
                }

                return consumedByScroll || sceneChanged ? UiEventResult.CreateHandled() : UiEventResult.Unhandled;
            }

            sceneChanged |= UpdatePointerState(pointerEvent, targetNode);
            sceneChanged |= UpdateFocusState(pointerEvent, targetNode);

            if (pointerEvent.PointerEventType == UiPointerEventType.Click)
            {
                sceneChanged |= UpdateSemanticState(targetNode);
            }
            else
            {
                if (sceneChanged)
                {
                    Version++;
                    IsDirty = true;
                }

                return targetNode != null || sceneChanged ? UiEventResult.CreateHandled() : UiEventResult.Unhandled;
            }
        }

        bool handled = false;
        UiNode? currentNode = targetNode;
        while (currentNode != null)
        {
            if (DispatchNodeActions(currentNode, evt))
            {
                handled = true;
                break;
            }

            currentNode = currentNode.Parent;
        }

        if (handled || sceneChanged)
        {
            Version++;
            IsDirty = true;
        }

        return handled || sceneChanged ? UiEventResult.CreateHandled() : UiEventResult.Unhandled;
    }

    public UiSceneDiff CreateFullDiff()
    {
        UiSceneSnapshot snapshot = new(Version, Root != null ? UiNodeDiff.FromNode(Root) : null);
        IsDirty = false;
        return new UiSceneDiff(UiSceneDiffKind.FullSnapshot, snapshot);
    }

    public UiNode? FindNode(UiNodeId id)
    {
        return Root == null ? null : FindNode(Root, id);
    }

    public UiNode? FindByElementId(string elementId)
    {
        return Root == null ? null : FindByElementId(Root, elementId);
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
        List<UiNode> matches = new();
        Traverse(Root, selectors, matches);
        return matches;
    }

    public UiNode? HitTest(float x, float y)
    {
        return Root == null ? null : HitTest(Root, x, y);
    }

    private UiNode BuildNode(UiElement element)
    {
        UiAttributeBag attributes = new();
        foreach (KeyValuePair<string, string> pair in element.Attributes)
        {
            attributes[pair.Key] = pair.Value;
        }

        string? elementId = attributes["id"];
        IReadOnlyList<string> classNames = attributes.GetClassList();
        UiNode[] children = element.Children.Select(BuildNode).ToArray();
        return new UiNode(
            new UiNodeId(_nextNodeId++),
            element.Kind,
            textContent: element.TextContent,
            children: children,
            tagName: element.TagName,
            elementId: elementId,
            classNames: classNames,
            attributes: attributes,
            inlineStyle: element.InlineStyle);
    }

    private static bool IsTruthy(string? value)
    {
        return !string.IsNullOrWhiteSpace(value)
            && (value.Equals("true", StringComparison.OrdinalIgnoreCase)
                || value.Equals("checked", StringComparison.OrdinalIgnoreCase)
                || value.Equals("selected", StringComparison.OrdinalIgnoreCase)
                || value.Equals("1", StringComparison.OrdinalIgnoreCase));
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

    private static bool HasBooleanAttribute(UiAttributeBag attributes, string name)
    {
        return attributes.Contains(name) || IsTruthy(attributes[name]);
    }

    private IReadOnlyList<UiStyleSheet> GetEffectiveStyleSheets()
    {
        List<UiStyleSheet> sheets = new(_styleSheets.Count + (Theme?.StyleSheets.Count ?? 0));
        sheets.AddRange(_styleSheets);
        if (Theme != null)
        {
            sheets.AddRange(Theme.StyleSheets);
        }

        return sheets;
    }

    private UiNode? ResolveTarget(UiEvent evt)
    {
        if (evt.TargetNodeId is UiNodeId targetNodeId && targetNodeId.IsValid)
        {
            return FindNode(targetNodeId);
        }

        if (evt is UiPointerEvent pointerEvent)
        {
            return HitTest(pointerEvent.X, pointerEvent.Y);
        }

        return null;
    }

    private bool DispatchNodeActions(UiNode node, UiEvent evt)
    {
        for (int i = 0; i < node.ActionHandles.Count; i++)
        {
            UiActionHandle actionHandle = node.ActionHandles[i];
            if (Dispatcher.Dispatch(actionHandle, new UiActionContext(this, evt, node)))
            {
                return true;
            }
        }

        return false;
    }

    private (bool Consumed, bool Changed) HandleScrollInteraction(UiPointerEvent evt, UiNode? targetNode)
    {
        if (_scrollDragNodeId is UiNodeId dragNodeId && dragNodeId.IsValid)
        {
            UiNode? dragNode = FindNode(dragNodeId);
            if (dragNode == null)
            {
                ClearScrollDrag();
                return (false, false);
            }

            return evt.PointerEventType switch
            {
                UiPointerEventType.Move => (true, UpdateScrollDrag(dragNode, evt.X, evt.Y)),
                UiPointerEventType.Up => (true, ClearActiveScrollDrag()),
                _ => (true, false)
            };
        }

        UiNode? scrollNode = ResolveScrollContainer(targetNode);
        if (scrollNode == null)
        {
            return (false, false);
        }

        if (evt.PointerEventType == UiPointerEventType.Scroll)
        {
            bool changed = scrollNode.ScrollBy(evt.DeltaX, evt.DeltaY);
            return (changed, changed);
        }

        if (evt.PointerEventType == UiPointerEventType.Down && TryStartScrollDrag(scrollNode, evt.X, evt.Y))
        {
            return (true, false);
        }

        if (evt.PointerEventType == UiPointerEventType.Up)
        {
            ClearScrollDrag();
        }

        return (false, false);
    }

    private UiNode? ResolveScrollContainer(UiNode? node)
    {
        UiNode? current = node;
        while (current != null)
        {
            if (current.Style.Overflow == UiOverflow.Scroll)
            {
                return current;
            }

            current = current.Parent;
        }

        return null;
    }

    private bool TryStartScrollDrag(UiNode node, float x, float y)
    {
        UiRect verticalThumb = UiScrollGeometry.GetVerticalThumbRect(node);
        if (verticalThumb.Width > 0f && verticalThumb.Height > 0f && verticalThumb.Contains(x, y))
        {
            _scrollDragNodeId = node.Id;
            _scrollDragAxis = UiScrollAxis.Vertical;
            _scrollDragPointerX = x;
            _scrollDragPointerY = y;
            _scrollDragStartOffsetX = node.ScrollOffsetX;
            _scrollDragStartOffsetY = node.ScrollOffsetY;
            return true;
        }

        UiRect horizontalThumb = UiScrollGeometry.GetHorizontalThumbRect(node);
        if (horizontalThumb.Width > 0f && horizontalThumb.Height > 0f && horizontalThumb.Contains(x, y))
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
                UiRect track = UiScrollGeometry.GetVerticalTrackRect(node);
                UiRect thumb = UiScrollGeometry.GetVerticalThumbRect(node);
                float travel = Math.Max(0f, track.Height - thumb.Height);
                if (travel <= 0.01f || node.MaxScrollY <= 0.01f)
                {
                    return false;
                }

                float delta = pointerY - _scrollDragPointerY;
                float offset = _scrollDragStartOffsetY + ((delta / travel) * node.MaxScrollY);
                return node.SetScrollOffset(node.ScrollOffsetX, offset);
            }
            case UiScrollAxis.Horizontal:
            {
                UiRect track = UiScrollGeometry.GetHorizontalTrackRect(node);
                UiRect thumb = UiScrollGeometry.GetHorizontalThumbRect(node);
                float travel = Math.Max(0f, track.Width - thumb.Width);
                if (travel <= 0.01f || node.MaxScrollX <= 0.01f)
                {
                    return false;
                }

                float delta = pointerX - _scrollDragPointerX;
                float offset = _scrollDragStartOffsetX + ((delta / travel) * node.MaxScrollX);
                return node.SetScrollOffset(offset, node.ScrollOffsetY);
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
        bool hadActiveDrag = _scrollDragNodeId is UiNodeId dragNodeId && dragNodeId.IsValid;
        ClearScrollDrag();
        return hadActiveDrag;
    }

    private bool UpdatePointerState(UiPointerEvent evt, UiNode? targetNode)
    {
        bool stateChanged = false;

        if (_hoveredNodeId is UiNodeId previousHoveredId && previousHoveredId.IsValid && previousHoveredId != targetNode?.Id)
        {
            FindNode(previousHoveredId)?.RemovePseudoState(UiPseudoState.Hover);
            stateChanged = true;
        }

        if (targetNode != null)
        {
            if (_hoveredNodeId != targetNode.Id)
            {
                targetNode.AddPseudoState(UiPseudoState.Hover);
                stateChanged = true;
            }

            _hoveredNodeId = targetNode.Id;
        }
        else if (_hoveredNodeId != null)
        {
            _hoveredNodeId = null;
            stateChanged = true;
        }

        if (evt.PointerEventType == UiPointerEventType.Down && targetNode != null)
        {
            if (_pressedNodeId is UiNodeId previousPressedId && previousPressedId.IsValid && previousPressedId != targetNode.Id)
            {
                FindNode(previousPressedId)?.RemovePseudoState(UiPseudoState.Active);
            }

            _pressedNodeId = targetNode.Id;
            targetNode.AddPseudoState(UiPseudoState.Active);
            stateChanged = true;
        }
        else if (evt.PointerEventType == UiPointerEventType.Up)
        {
            if (_pressedNodeId is UiNodeId pressedId && pressedId.IsValid)
            {
                FindNode(pressedId)?.RemovePseudoState(UiPseudoState.Active);
                stateChanged = true;
            }

            _pressedNodeId = null;
        }

        return stateChanged;
    }

    private bool UpdateFocusState(UiPointerEvent evt, UiNode? targetNode)
    {
        return evt.PointerEventType switch
        {
            UiPointerEventType.Down or UiPointerEventType.Click => SetFocusedNode(ResolveFocusableNode(targetNode)),
            _ => false
        };
    }

    private bool UpdateSemanticState(UiNode? targetNode)
    {
        UiNode? semanticNode = ResolveSemanticNode(targetNode);
        if (semanticNode == null || semanticNode.PseudoState.HasFlag(UiPseudoState.Disabled))
        {
            return false;
        }

        if (IsRadioNode(semanticNode))
        {
            bool changed = false;
            string? groupName = semanticNode.Attributes["name"];
            if (!string.IsNullOrWhiteSpace(groupName) && Root != null)
            {
                foreach (UiNode node in EnumerateNodes(Root))
                {
                    if (node == semanticNode || !IsRadioNode(node))
                    {
                        continue;
                    }

                    if (string.Equals(node.Attributes["name"], groupName, StringComparison.OrdinalIgnoreCase))
                    {
                        changed |= SetCheckedState(node, false);
                    }
                }
            }

            changed |= SetCheckedState(semanticNode, true);
            if (!string.IsNullOrWhiteSpace(groupName))
            {
                changed |= RefreshRadioGroupValidation(groupName);
            }
            else
            {
                changed |= RefreshValidationState(semanticNode);
            }

            return changed;
        }

        if (IsCheckableNode(semanticNode))
        {
            bool isChecked = semanticNode.PseudoState.HasFlag(UiPseudoState.Checked);
            bool changed = SetCheckedState(semanticNode, !isChecked);
            changed |= RefreshValidationState(semanticNode);
            return changed;
        }

        return false;
    }

    private bool SetFocusedNode(UiNode? node)
    {
        UiNodeId? nextFocusedId = node?.Id;
        if (_focusedNodeId == nextFocusedId)
        {
            return false;
        }

        if (_focusedNodeId is UiNodeId previousFocusedId && previousFocusedId.IsValid)
        {
            FindNode(previousFocusedId)?.RemovePseudoState(UiPseudoState.Focus);
        }

        _focusedNodeId = nextFocusedId;
        if (node != null)
        {
            node.AddPseudoState(UiPseudoState.Focus);
        }

        return true;
    }

    private UiNode? ResolveFocusableNode(UiNode? node)
    {
        UiNode? current = node;
        while (current != null)
        {
            if (IsFocusableNode(current))
            {
                return current;
            }

            current = current.Parent;
        }

        return null;
    }

    private UiNode? ResolveSemanticNode(UiNode? node)
    {
        UiNode? current = node;
        while (current != null)
        {
            if (IsCheckableNode(current) || IsRadioNode(current))
            {
                return current;
            }

            current = current.Parent;
        }

        return null;
    }

    private bool RefreshRadioGroupValidation(string groupName)
    {
        if (string.IsNullOrWhiteSpace(groupName) || Root == null)
        {
            return false;
        }

        bool changed = false;
        foreach (UiNode node in EnumerateNodes(Root))
        {
            if (IsRadioNode(node) && string.Equals(node.Attributes["name"], groupName, StringComparison.OrdinalIgnoreCase))
            {
                changed |= RefreshValidationState(node);
            }
        }

        return changed;
    }

    private bool RefreshValidationState(UiNode node)
    {
        bool changed = false;
        bool required = IsRequiredNode(node);
        changed |= SetPseudoFlag(node, UiPseudoState.Required, required);

        bool invalid = EvaluateInvalidState(node, required);
        changed |= SetPseudoFlag(node, UiPseudoState.Invalid, invalid);

        if (required)
        {
            node.Attributes["aria-required"] = "true";
        }

        if (invalid)
        {
            node.Attributes["aria-invalid"] = "true";
        }
        else if (IsConstraintValidatedNode(node) || IsCheckableNode(node) || IsRadioNode(node) || node.Attributes.Contains("aria-invalid"))
        {
            node.Attributes["aria-invalid"] = "false";
        }

        return changed;
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

            string? groupName = node.Attributes["name"];
            if (!string.IsNullOrWhiteSpace(groupName) && Root != null)
            {
                foreach (UiNode candidate in EnumerateNodes(Root))
                {
                    if (IsRadioNode(candidate)
                        && string.Equals(candidate.Attributes["name"], groupName, StringComparison.OrdinalIgnoreCase)
                        && candidate.PseudoState.HasFlag(UiPseudoState.Checked))
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

        string? value = ResolveConstraintValue(node);
        bool isEmpty = string.IsNullOrWhiteSpace(value);
        if (required && isEmpty)
        {
            return true;
        }

        if (isEmpty)
        {
            return false;
        }

        return ViolatesInputConstraints(node, value!);
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
            string? groupName = node.Attributes["name"];
            if (!string.IsNullOrWhiteSpace(groupName))
            {
                foreach (UiNode candidate in EnumerateNodes(Root))
                {
                    if (IsRadioNode(candidate)
                        && string.Equals(candidate.Attributes["name"], groupName, StringComparison.OrdinalIgnoreCase)
                        && (candidate.Attributes.Contains("required") || IsTruthy(candidate.Attributes["aria-required"])))
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
        bool hasFlag = node.PseudoState.HasFlag(flag);
        if (hasFlag == value)
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
        return node.Kind is UiNodeKind.Input or UiNodeKind.Select or UiNodeKind.TextArea or UiNodeKind.Slider
            || (string.Equals(node.TagName, "input", StringComparison.OrdinalIgnoreCase)
                && !IsCheckableNode(node)
                && !IsRadioNode(node)
                && !IsInputType(node, "button")
                && !IsInputType(node, "submit")
                && !IsInputType(node, "reset"));
    }

    private static string? ResolveConstraintValue(UiNode node)
    {
        string? value = node.Attributes["value"];
        if (string.IsNullOrWhiteSpace(value) && node.Kind == UiNodeKind.TextArea)
        {
            value = node.TextContent;
        }

        return value;
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

        string inputType = GetNormalizedInputType(node);
        return inputType switch
        {
            "email" => !BasicEmailPattern.IsMatch(value),
            "number" or "range" => ViolatesNumericConstraint(node, value),
            "url" => !Uri.TryCreate(value, UriKind.Absolute, out _),
            _ => false
        };
    }

    private static bool ViolatesLengthConstraint(UiNode node, string value)
    {
        if (TryParseIntegerAttribute(node, "minlength", out int minLength) && value.Length < minLength)
        {
            return true;
        }

        return TryParseIntegerAttribute(node, "maxlength", out int maxLength) && value.Length > maxLength;
    }

    private static bool ViolatesPatternConstraint(UiNode node, string value)
    {
        string? pattern = node.Attributes["pattern"];
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return false;
        }

        try
        {
            Regex regex = new($"^(?:{pattern})$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));
            return !regex.IsMatch(value);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static bool ViolatesNumericConstraint(UiNode node, string value)
    {
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double numericValue))
        {
            return true;
        }

        if (TryParseFloatAttribute(node, "min", out double minValue) && numericValue < minValue)
        {
            return true;
        }

        if (TryParseFloatAttribute(node, "max", out double maxValue) && numericValue > maxValue)
        {
            return true;
        }

        string? stepText = node.Attributes["step"];
        if (string.IsNullOrWhiteSpace(stepText) || string.Equals(stepText, "any", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!double.TryParse(stepText, NumberStyles.Float, CultureInfo.InvariantCulture, out double stepValue) || stepValue <= 0d)
        {
            return false;
        }

        double stepBase = TryParseFloatAttribute(node, "min", out double parsedMin) ? parsedMin : 0d;
        double quotient = (numericValue - stepBase) / stepValue;
        double distance = Math.Abs(quotient - Math.Round(quotient));
        double tolerance = Math.Max(1e-6d, Math.Abs(stepValue) * 1e-6d);
        return distance > tolerance;
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
        string? raw = node.Attributes[attributeName];
        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryParseFloatAttribute(UiNode node, string attributeName, out double value)
    {
        string? raw = node.Attributes[attributeName];
        return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
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

        return node.Kind is UiNodeKind.Button
            or UiNodeKind.Input
            or UiNodeKind.Checkbox
            or UiNodeKind.Radio
            or UiNodeKind.Toggle
            or UiNodeKind.Slider
            or UiNodeKind.Select
            or UiNodeKind.TextArea;
    }

    private static bool IsCheckableNode(UiNode node)
    {
        return node.Kind is UiNodeKind.Checkbox or UiNodeKind.Toggle || IsInputType(node, "checkbox");
    }

    private static bool IsRadioNode(UiNode node)
    {
        return node.Kind == UiNodeKind.Radio || IsInputType(node, "radio");
    }

    private static bool IsInputType(UiNode node, string type)
    {
        return string.Equals(node.TagName, "input", StringComparison.OrdinalIgnoreCase)
            && string.Equals(node.Attributes["type"], type, StringComparison.OrdinalIgnoreCase);
    }

    private static bool SetCheckedState(UiNode node, bool value)
    {
        bool isChecked = node.PseudoState.HasFlag(UiPseudoState.Checked);
        if (isChecked == value)
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
        Stack<UiNode> stack = new();
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

        foreach (UiNode childNode in node.Children)
        {
            Traverse(childNode, selectors, matches);
        }
    }

    private static UiNode? FindNode(UiNode currentNode, UiNodeId targetNodeId)
    {
        if (currentNode.Id == targetNodeId)
        {
            return currentNode;
        }

        foreach (UiNode childNode in currentNode.Children)
        {
            UiNode? matchedNode = FindNode(childNode, targetNodeId);
            if (matchedNode != null)
            {
                return matchedNode;
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

        foreach (UiNode childNode in currentNode.Children)
        {
            UiNode? matchedNode = FindByElementId(childNode, elementId);
            if (matchedNode != null)
            {
                return matchedNode;
            }
        }

        return null;
    }

    private static UiNode? HitTest(UiNode node, float x, float y)
    {
        return HitTest(node, x, y, SKMatrix.Identity);
    }

    private static UiNode? HitTest(UiNode node, float x, float y, SKMatrix accumulatedTransform)
    {
        UiStyle style = node.RenderStyle;
        if (!style.Visible || style.Display == UiDisplay.None)
        {
            return null;
        }

        SKMatrix nodeTransform = style.Transform.HasOperations
            ? SKMatrix.Concat(accumulatedTransform, UiTransformMath.CreateMatrix(style, node.LayoutRect))
            : accumulatedTransform;

        SKPoint localPoint = new(x, y);
        if (!UiTransformMath.TryInvert(nodeTransform, out SKMatrix inverse))
        {
            return null;
        }

        localPoint = inverse.MapPoint(localPoint);
        bool containsPoint = node.LayoutRect.Contains(localPoint.X, localPoint.Y);
        bool clipsChildren = style.ClipContent || style.Overflow == UiOverflow.Scroll;
        if (!containsPoint && clipsChildren)
        {
            return null;
        }

        SKMatrix childTransform = style.Overflow == UiOverflow.Scroll
            ? SKMatrix.Concat(nodeTransform, SKMatrix.CreateTranslation(-node.ScrollOffsetX, -node.ScrollOffsetY))
            : nodeTransform;

        foreach (UiNode child in UiVisualTreeOrdering.FrontToBack(node.Children))
        {
            UiNode? hitChild = HitTest(child, x, y, childTransform);
            if (hitChild != null)
            {
                return hitChild;
            }
        }

        return containsPoint ? node : null;
    }
}
