using Ludots.UI.Runtime.Actions;
using Ludots.UI.Runtime.Diff;
using Ludots.UI.Runtime.Events;

namespace Ludots.UI.Runtime;

public sealed class UiScene
{
    private readonly UiStyleResolver _styleResolver = new();
    private readonly UiLayoutEngine _layoutEngine = new();
    private readonly List<UiStyleSheet> _styleSheets = new();
    private UiNodeId? _hoveredNodeId;
    private UiNodeId? _pressedNodeId;
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

    public long Version { get; private set; }

    public bool IsDirty { get; private set; }

    public void Mount(UiNode root)
    {
        Root = root ?? throw new ArgumentNullException(nameof(root));
        Document = null;
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
        Root = BuildNode(document.Root);
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

    public UiEventResult Dispatch(UiEvent evt)
    {
        ArgumentNullException.ThrowIfNull(evt);

        if (Root == null)
        {
            return UiEventResult.Unhandled;
        }

        UiNode? targetNode = ResolveTarget(evt);
        if (evt is UiPointerEvent pointerEvent)
        {
            UpdatePointerState(pointerEvent, targetNode);
            if (pointerEvent.PointerEventType != UiPointerEventType.Click)
            {
                return targetNode != null ? UiEventResult.CreateHandled() : UiEventResult.Unhandled;
            }
        }

        if (targetNode == null)
        {
            return UiEventResult.Unhandled;
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

        if (handled)
        {
            Version++;
            IsDirty = true;
        }

        return handled ? UiEventResult.CreateHandled() : UiEventResult.Unhandled;
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
        UiNode node = new(
            new UiNodeId(_nextNodeId++),
            element.Kind,
            textContent: element.TextContent,
            children: children,
            tagName: element.TagName,
            elementId: elementId,
            classNames: classNames,
            attributes: attributes,
            inlineStyle: element.InlineStyle);

        if (attributes.Contains("disabled"))
        {
            node.AddPseudoState(UiPseudoState.Disabled);
        }

        if (attributes.Contains("checked") || IsTruthy(attributes["checked"]))
        {
            node.AddPseudoState(UiPseudoState.Checked);
        }

        if (attributes.Contains("selected") || IsTruthy(attributes["selected"]) || IsTruthy(attributes["aria-selected"]))
        {
            node.AddPseudoState(UiPseudoState.Selected);
        }

        return node;
    }

    private static bool IsTruthy(string? value)
    {
        return !string.IsNullOrWhiteSpace(value)
            && (value.Equals("true", StringComparison.OrdinalIgnoreCase)
                || value.Equals("checked", StringComparison.OrdinalIgnoreCase)
                || value.Equals("selected", StringComparison.OrdinalIgnoreCase)
                || value.Equals("1", StringComparison.OrdinalIgnoreCase));
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

    private void UpdatePointerState(UiPointerEvent evt, UiNode? targetNode)
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

        if (stateChanged)
        {
            IsDirty = true;
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
        if (!node.Style.Visible || node.Style.Display == UiDisplay.None || !node.LayoutRect.Contains(x, y))
        {
            return null;
        }

        for (int i = node.Children.Count - 1; i >= 0; i--)
        {
            UiNode? hitChild = HitTest(node.Children[i], x, y);
            if (hitChild != null)
            {
                return hitChild;
            }
        }

        return node;
    }
}

