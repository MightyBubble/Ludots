using Ludots.UI.Runtime.Actions;
using SkiaSharp;

namespace Ludots.UI.Runtime;

public sealed class UiNode
{
    private readonly UiNode[] _children;
    private readonly List<UiActionHandle> _actionHandles;
    private readonly string[] _classNames;
    private readonly List<UiTransitionChannelState> _transitionChannels = new();
    private readonly List<UiAnimationChannelState> _animationChannels = new();
    private bool _hasComputedStyle;

    public UiNode(
        UiNodeId id,
        UiNodeKind kind,
        UiStyle? style = null,
        string? textContent = null,
        IEnumerable<UiNode>? children = null,
        IEnumerable<UiActionHandle>? actionHandles = null,
        string? tagName = null,
        string? elementId = null,
        IEnumerable<string>? classNames = null,
        UiAttributeBag? attributes = null,
        UiStyleDeclaration? inlineStyle = null,
        UiCanvasContent? canvasContent = null)
    {
        if (!id.IsValid)
        {
            throw new ArgumentException("UiNodeId must be valid.", nameof(id));
        }

        Id = id;
        Kind = kind;
        LocalStyle = style ?? UiStyle.Default;
        Style = LocalStyle;
        RenderStyle = LocalStyle;
        TextContent = textContent;
        TagName = string.IsNullOrWhiteSpace(tagName) ? GetDefaultTagName(kind) : tagName;
        ElementId = !string.IsNullOrWhiteSpace(elementId) ? elementId : LocalStyle.Id;

        Attributes = attributes ?? new UiAttributeBag();
        if (!string.IsNullOrWhiteSpace(ElementId))
        {
            Attributes["id"] = ElementId;
        }

        _classNames = classNames?.Where(static value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
            ?? SplitClasses(LocalStyle.ClassName);
        if (_classNames.Length > 0)
        {
            Attributes["class"] = string.Join(' ', _classNames);
        }

        InlineStyle = inlineStyle ?? new UiStyleDeclaration();
        CanvasContent = canvasContent;
        _children = children?.ToArray() ?? Array.Empty<UiNode>();
        _actionHandles = actionHandles?.Where(handle => handle.IsValid).ToList() ?? new List<UiActionHandle>();

        for (int i = 0; i < _children.Length; i++)
        {
            _children[i].Parent = this;
        }
    }

    public UiNodeId Id { get; }

    public UiNodeKind Kind { get; }

    public UiNode? Parent { get; private set; }

    public string TagName { get; }

    public string? ElementId { get; }

    public UiAttributeBag Attributes { get; }

    public UiStyleDeclaration InlineStyle { get; }

    public UiStyle LocalStyle { get; }

    public UiStyle Style { get; private set; }

    public UiStyle RenderStyle { get; private set; }

    public UiPseudoState PseudoState { get; private set; }

    public UiRect LayoutRect { get; private set; }

    public float ScrollOffsetX { get; private set; }

    public float ScrollOffsetY { get; private set; }

    public float ScrollContentWidth { get; private set; }

    public float ScrollContentHeight { get; private set; }

    public string? TextContent { get; }

    public UiCanvasContent? CanvasContent { get; private set; }

    public IReadOnlyList<string> ClassNames => _classNames;

    public IReadOnlyList<UiNode> Children => _children;

    public IReadOnlyList<UiActionHandle> ActionHandles => _actionHandles;

    public float MaxScrollX => Math.Max(0f, ScrollContentWidth - LayoutRect.Width);

    public float MaxScrollY => Math.Max(0f, ScrollContentHeight - LayoutRect.Height);

    public bool CanScrollHorizontally => Style.Overflow == UiOverflow.Scroll && MaxScrollX > 0.01f;

    public bool CanScrollVertically => Style.Overflow == UiOverflow.Scroll && MaxScrollY > 0.01f;

    public bool HasClass(string className)
    {
        return _classNames.Contains(className, StringComparer.OrdinalIgnoreCase);
    }

    internal void SetComputedStyle(UiStyle style, UiAnimationSpec? animation = null)
    {
        ArgumentNullException.ThrowIfNull(style);

        UiStyle previousTarget = Style;
        Style = style;

        if (!_hasComputedStyle)
        {
            _transitionChannels.Clear();
            RestartAnimations(style, animation);
            RenderStyle = ComposeRenderStyle();
            _hasComputedStyle = true;
            return;
        }

        if (!HasMeaningfulStyleChange(previousTarget, style))
        {
            return;
        }

        BeginVisualTransitions(previousTarget, style);
        RestartAnimations(style, animation);
        RenderStyle = ComposeRenderStyle();
    }

    internal bool AdvanceTransitions(float deltaSeconds)
    {
        if (deltaSeconds <= 0f)
        {
            return false;
        }

        for (int i = _transitionChannels.Count - 1; i >= 0; i--)
        {
            UiTransitionChannelState channel = _transitionChannels[i];
            channel.Advance(deltaSeconds);
            if (channel.IsCompleted)
            {
                _transitionChannels.RemoveAt(i);
            }
        }

        for (int i = _animationChannels.Count - 1; i >= 0; i--)
        {
            UiAnimationChannelState channel = _animationChannels[i];
            channel.Advance(deltaSeconds);
            if (channel.IsDiscardable)
            {
                _animationChannels.RemoveAt(i);
            }
        }

        UiStyle nextRenderStyle = ComposeRenderStyle();
        if (Equals(RenderStyle, nextRenderStyle))
        {
            return false;
        }

        RenderStyle = nextRenderStyle;
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
        float clampedX = Math.Clamp(offsetX, 0f, MaxScrollX);
        float clampedY = Math.Clamp(offsetY, 0f, MaxScrollY);
        if (Math.Abs(clampedX - ScrollOffsetX) < 0.01f && Math.Abs(clampedY - ScrollOffsetY) < 0.01f)
        {
            return false;
        }

        ScrollOffsetX = clampedX;
        ScrollOffsetY = clampedY;
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
        PseudoState &= ~state;
    }

    public void SetCanvasContent(UiCanvasContent? canvasContent)
    {
        CanvasContent = canvasContent;
    }

    private static string GetDefaultTagName(UiNodeKind kind)
    {
        return kind switch
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
            _ => "div"
        };
    }

    private static string[] SplitClasses(string? classText)
    {
        if (string.IsNullOrWhiteSpace(classText))
        {
            return Array.Empty<string>();
        }

        return classText
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool HasMeaningfulStyleChange(UiStyle previous, UiStyle current)
    {
        return !Equals(previous with { Transition = null }, current with { Transition = null });
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
            UiAnimationChannelState channel = new(animation.Entries[i], style);
            if (channel.HasTracks)
            {
                _animationChannels.Add(channel);
            }
        }
    }

    private UiStyle ComposeRenderStyle()
    {
        UiStyle nextRenderStyle = Style;
        for (int i = 0; i < _transitionChannels.Count; i++)
        {
            nextRenderStyle = UiTransitionMath.Apply(nextRenderStyle, _transitionChannels[i]);
        }

        for (int i = 0; i < _animationChannels.Count; i++)
        {
            nextRenderStyle = _animationChannels[i].Apply(nextRenderStyle);
        }

        return nextRenderStyle;
    }

    private void BeginVisualTransitions(UiStyle previousTarget, UiStyle targetStyle)
    {
        UiTransitionSpec? transition = targetStyle.Transition ?? previousTarget.Transition;
        if (transition == null)
        {
            _transitionChannels.Clear();
            return;
        }

        List<UiTransitionChannelState> nextChannels = new();
        UiStyle nextRenderStyle = targetStyle;

        QueueColorTransition(transition, "background-color", RenderStyle.BackgroundColor, targetStyle.BackgroundColor, ref nextRenderStyle, nextChannels);
        QueueColorTransition(transition, "border-color", RenderStyle.BorderColor, targetStyle.BorderColor, ref nextRenderStyle, nextChannels);
        QueueColorTransition(transition, "outline-color", RenderStyle.OutlineColor, targetStyle.OutlineColor, ref nextRenderStyle, nextChannels);
        QueueColorTransition(transition, "color", RenderStyle.Color, targetStyle.Color, ref nextRenderStyle, nextChannels);
        QueueFloatTransition(transition, "opacity", RenderStyle.Opacity, targetStyle.Opacity, ref nextRenderStyle, nextChannels);
        QueueFloatTransition(transition, "filter", RenderStyle.FilterBlurRadius, targetStyle.FilterBlurRadius, ref nextRenderStyle, nextChannels);
        QueueFloatTransition(transition, "backdrop-filter", RenderStyle.BackdropBlurRadius, targetStyle.BackdropBlurRadius, ref nextRenderStyle, nextChannels);

        _transitionChannels.Clear();
        _transitionChannels.AddRange(nextChannels);
    }

    private static void QueueFloatTransition(
        UiTransitionSpec transition,
        string propertyName,
        float startValue,
        float endValue,
        ref UiStyle renderStyle,
        ICollection<UiTransitionChannelState> channels)
    {
        if (Math.Abs(startValue - endValue) < 0.001f)
        {
            return;
        }

        if (!transition.TryGet(propertyName, out UiTransitionEntry? entry) || entry == null || entry.DurationSeconds <= 0f)
        {
            return;
        }

        channels.Add(new UiTransitionChannelState(propertyName, entry.DurationSeconds, entry.DelaySeconds, entry.Easing, startValue, endValue));
        renderStyle = UiTransitionMath.ApplyFloat(renderStyle, propertyName, startValue);
    }

    private static void QueueColorTransition(
        UiTransitionSpec transition,
        string propertyName,
        SKColor startValue,
        SKColor endValue,
        ref UiStyle renderStyle,
        ICollection<UiTransitionChannelState> channels)
    {
        if (startValue == endValue)
        {
            return;
        }

        if (!transition.TryGet(propertyName, out UiTransitionEntry? entry) || entry == null || entry.DurationSeconds <= 0f)
        {
            return;
        }

        channels.Add(new UiTransitionChannelState(propertyName, entry.DurationSeconds, entry.DelaySeconds, entry.Easing, startValue, endValue));
        renderStyle = UiTransitionMath.ApplyColor(renderStyle, propertyName, startValue);
    }
}
