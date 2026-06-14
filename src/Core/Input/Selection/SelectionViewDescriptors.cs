using Arch.Core;

namespace Ludots.Core.Input.Selection
{
    public readonly record struct SelectionContainerDescriptor(
        Entity Container,
        Entity Owner,
        string SetKey,
        SelectionContainerKind Kind,
        uint Revision,
        int MemberCount,
        Entity Primary);

    public readonly record struct SelectionViewDescriptor(
        Entity Viewer,
        string ViewKey,
        SelectionContainerDescriptor Container);
}
