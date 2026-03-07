using Ludots.UI.Runtime.Actions;

namespace Ludots.UI.Runtime.Diff;

public sealed class UiNodeDiff
{
    public UiNodeDiff(
        UiNodeId id,
        UiNodeKind kind,
        UiStyle style,
        string? textContent,
        IReadOnlyList<UiActionHandle> actionHandles,
        IReadOnlyList<UiNodeDiff> children,
        string tagName,
        string? elementId,
        IReadOnlyList<string> classNames,
        UiRect layoutRect)
    {
        Id = id;
        Kind = kind;
        Style = style;
        TextContent = textContent;
        ActionHandles = actionHandles;
        Children = children;
        TagName = tagName;
        ElementId = elementId;
        ClassNames = classNames;
        LayoutRect = layoutRect;
    }

    public UiNodeId Id { get; }

    public UiNodeKind Kind { get; }

    public UiStyle Style { get; }

    public string? TextContent { get; }

    public IReadOnlyList<UiActionHandle> ActionHandles { get; }

    public IReadOnlyList<UiNodeDiff> Children { get; }

    public string TagName { get; }

    public string? ElementId { get; }

    public IReadOnlyList<string> ClassNames { get; }

    public UiRect LayoutRect { get; }

    public static UiNodeDiff FromNode(UiNode node)
    {
        UiNodeDiff[] childDiffs = node.Children.Select(FromNode).ToArray();
        UiActionHandle[] actionHandles = node.ActionHandles.ToArray();
        return new UiNodeDiff(node.Id, node.Kind, node.Style, node.TextContent, actionHandles, childDiffs, node.TagName, node.ElementId, node.ClassNames.ToArray(), node.LayoutRect);
    }
}
