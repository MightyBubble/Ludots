using Arch.Core;
using Ludots.Core.Engine;
using Ludots.Core.UI.EntityCommandPanels;

namespace UxPrototypeMod.Runtime;

internal sealed class UxPrototypeEntityCommandPanelSource : IEntityCommandPanelSource, IEntityCommandPanelActionSource
{
    public const string SourceId = "uxprototype.entity-actions";

    private readonly GameEngine _engine;
    private readonly UxPrototypeScenarioState _state;

    public UxPrototypeEntityCommandPanelSource(GameEngine engine, UxPrototypeScenarioState state)
    {
        _engine = engine;
        _state = state;
    }

    public bool TryGetRevision(Entity target, out uint revision)
    {
        revision = 0;
        if (!_state.TryBuildEntityCommandSlots(_engine, target, out string header, out var slots))
        {
            return false;
        }

        unchecked
        {
            uint hash = 2166136261u;
            hash = (hash ^ (uint)target.Id) * 16777619u;
            hash = (hash ^ (uint)target.Version) * 16777619u;
            hash = (hash ^ (uint)header.GetHashCode(System.StringComparison.Ordinal)) * 16777619u;
            for (int i = 0; i < slots.Count; i++)
            {
                var slot = slots[i];
                hash = (hash ^ (uint)slot.ActionId.GetHashCode(System.StringComparison.Ordinal)) * 16777619u;
                hash = (hash ^ (uint)slot.Label.GetHashCode(System.StringComparison.Ordinal)) * 16777619u;
                hash = (hash ^ (uint)slot.CountText.GetHashCode(System.StringComparison.Ordinal)) * 16777619u;
                hash = (hash ^ (slot.Enabled ? 1u : 0u)) * 16777619u;
                hash = (hash ^ (slot.Active ? 1u : 0u)) * 16777619u;
            }

            revision = hash;
        }

        return true;
    }

    public int GetGroupCount(Entity target)
    {
        return _state.TryBuildEntityCommandSlots(_engine, target, out _, out _) ? 1 : 0;
    }

    public bool TryGetGroup(Entity target, int groupIndex, out EntityCommandPanelGroupView group)
    {
        group = default;
        if (groupIndex != 0 || !_state.TryBuildEntityCommandSlots(_engine, target, out _, out var slots))
        {
            return false;
        }

        group = new EntityCommandPanelGroupView(0, "Commands", (byte)slots.Count);
        return true;
    }

    public int CopySlots(Entity target, int groupIndex, Span<EntityCommandPanelSlotView> destination)
    {
        if (groupIndex != 0 ||
            destination.IsEmpty ||
            !_state.TryBuildEntityCommandSlots(_engine, target, out _, out var slots))
        {
            return 0;
        }

        int count = System.Math.Min(destination.Length, slots.Count);
        for (int i = 0; i < count; i++)
        {
            var slot = slots[i];
            EntityCommandSlotStateFlags flags = slot.Enabled
                ? EntityCommandSlotStateFlags.Base
                : EntityCommandSlotStateFlags.Base | EntityCommandSlotStateFlags.Empty;
            if (slot.Active)
            {
                flags |= EntityCommandSlotStateFlags.GrantedOverride;
            }

            destination[i] = new EntityCommandPanelSlotView(
                i,
                0,
                0,
                flags,
                0,
                0,
                0,
                slot.Label,
                slot.Summary,
                slot.ActionId);
        }

        return count;
    }

    public bool ActivateSlot(Entity target, int groupIndex, int slotIndex)
    {
        return groupIndex == 0 && _state.TryActivateEntityCommand(_engine, target, slotIndex);
    }
}
