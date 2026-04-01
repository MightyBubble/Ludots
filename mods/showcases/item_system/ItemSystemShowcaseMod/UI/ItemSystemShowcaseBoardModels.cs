using Arch.Core;

namespace ItemSystemShowcaseMod.UI;

internal enum ItemSystemShowcaseBoardKind
{
    Grid,
    Slots,
    Recipes
}

internal enum ItemSystemShowcaseClickTargetKind
{
    None,
    GridCell,
    SlotCell,
    Recipe
}

internal sealed record ItemSystemShowcaseClickTarget(
    ItemSystemShowcaseClickTargetKind Kind,
    Entity Item,
    Entity Container,
    string Id,
    int GridX,
    int GridY);

internal sealed record ItemSystemShowcaseBoardCellModel(
    string PrimaryText,
    string SecondaryText,
    string FillColor,
    string BorderColor,
    bool IsSelected,
    ItemSystemShowcaseClickTarget Target);

internal sealed record ItemSystemShowcaseBoardModel(
    string Title,
    string AccentColor,
    ItemSystemShowcaseBoardKind Kind,
    ItemSystemShowcaseBoardCellModel[][] Rows);
