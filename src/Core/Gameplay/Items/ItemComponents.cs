using Arch.Core;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS;

namespace Ludots.Core.Gameplay.Items
{
    public enum ItemPlacementKind : byte
    {
        None = 0,
        Grid = 1,
        NamedSlot = 2
    }

    public struct ItemInstanceCm
    {
        public int DefinitionId;
        public int StackCount;
        public int Charges;
        public int Durability;
    }

    public struct ItemLocationCm
    {
        public Entity Container;
        public ItemPlacementKind PlacementKind;
        public short GridX;
        public short GridY;
        public short NamedSlotIndex;
        public byte RotationQuarterTurns;
    }

    public struct ItemContainerCm
    {
        public int LayoutId;
        public Entity Owner;
        public ItemContainerOwnerKind OwnerKind;
        public ItemContainerPurpose Purpose;
    }

    public struct ItemMountedContainerCm
    {
        public Entity ParentItem;
        public short MountIndex;
    }

    public struct InventoryEquipmentDirtyTag
    {
    }

    public unsafe struct ItemGrantedSlotBuffer
    {
        public const int CAPACITY = AbilityStateBuffer.CAPACITY;

        public fixed int AbilityIds[CAPACITY];
        public fixed int SourceItemIds[CAPACITY];
        public fixed int SourceItemWorldIds[CAPACITY];
        public fixed int SourceItemVersions[CAPACITY];

        public void ClearAll()
        {
            for (int i = 0; i < CAPACITY; i++)
            {
                AbilityIds[i] = 0;
                SourceItemIds[i] = 0;
                SourceItemWorldIds[i] = 0;
                SourceItemVersions[i] = 0;
            }
        }

        public void SetOverride(int slotIndex, int abilityId, Entity sourceItem)
        {
            if ((uint)slotIndex >= CAPACITY)
            {
                return;
            }

            AbilityIds[slotIndex] = abilityId;
            SourceItemIds[slotIndex] = sourceItem.Id;
            SourceItemWorldIds[slotIndex] = sourceItem.WorldId;
            SourceItemVersions[slotIndex] = sourceItem.Version;
        }

        public bool HasOverride(int slotIndex)
        {
            return (uint)slotIndex < CAPACITY && AbilityIds[slotIndex] > 0;
        }

        public AbilitySlotState GetOverride(int slotIndex)
        {
            if ((uint)slotIndex >= CAPACITY)
            {
                return default;
            }

            return new AbilitySlotState
            {
                AbilityId = AbilityIds[slotIndex],
                TemplateEntityId = 0,
                TemplateEntityWorldId = 0,
                TemplateEntityVersion = 0
            };
        }

        public Entity GetSourceItem(int slotIndex)
        {
            if ((uint)slotIndex >= CAPACITY)
            {
                return Entity.Null;
            }

            return EntityUtil.Reconstruct(SourceItemIds[slotIndex], SourceItemWorldIds[slotIndex], SourceItemVersions[slotIndex]);
        }

        public bool ContentEquals(in ItemGrantedSlotBuffer other)
        {
            for (int i = 0; i < CAPACITY; i++)
            {
                if (AbilityIds[i] != other.AbilityIds[i] ||
                    SourceItemIds[i] != other.SourceItemIds[i] ||
                    SourceItemWorldIds[i] != other.SourceItemWorldIds[i] ||
                    SourceItemVersions[i] != other.SourceItemVersions[i])
                {
                    return false;
                }
            }

            return true;
        }
    }
}
