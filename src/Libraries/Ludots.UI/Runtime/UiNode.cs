using Ludots.UI.Runtime.Actions;

namespace Ludots.UI.Runtime;

public sealed class UiNode
{
    private readonly UiNode[] _children;
    private readonly List<UiActionHandle> _actionHandles;
    private readonly string[] _classNames;

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
        UiStyleDeclaration? inlineStyle = null)
    {
        if (!id.IsValid)
        {
            throw new ArgumentException("UiNodeId must be valid.", nameof(id));
        }

        Id = id;
        Kind = kind;
        LocalStyle = style ?? UiStyle.Default;
        Style = LocalStyle;
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

    public UiPseudoState PseudoState { get; private set; }

    public UiRect LayoutRect { get; private set; }

    public string? TextContent { get; }

    public IReadOnlyList<string> ClassNames => _classNames;

    public IReadOnlyList<UiNode> Children => _children;

    public IReadOnlyList<UiActionHandle> ActionHandles => _actionHandles;

    public bool HasClass(string className)
    {
        return _classNames.Contains(className, StringComparer.OrdinalIgnoreCase);
    }

    internal void SetComputedStyle(UiStyle style)
    {
        Style = style ?? throw new ArgumentNullException(nameof(style));
    }

    internal void SetLayout(UiRect rect)
    {
        LayoutRect = rect;
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
}


