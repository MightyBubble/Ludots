using System;
using Arch.Core;

namespace Ludots.Core.Gameplay.GAS.Components
{
    public struct AbilitySlotState
    {
        public int AbilityId;
        public int TemplateEntityId;
        public int TemplateEntityWorldId;
        public int TemplateEntityVersion;
    }

    /// <summary>
    /// Stores the runtime state of abilities on a Unit.
    /// Flyweight pattern: refers to Template Entity for logic.
    /// 
    /// Two-layer design:
    ///   - AbilityStateBuffer (this) = base slots — permanent abilities configured at creation.
    ///   - GrantedSlotBuffer = temporary overrides granted by items/buffs.
    ///   - Resolving a slot: if GrantedSlotBuffer has an override for slot i, use it; else use base.
    /// </summary>
    public unsafe struct AbilityStateBuffer
    {
        public const int CAPACITY = 8;
        
        public fixed int AbilityIds[CAPACITY];
        public fixed int TemplateIds[CAPACITY];
        public fixed int TemplateWorldIds[CAPACITY];
        public fixed int TemplateVersions[CAPACITY];
        public int Count;

        public void AddAbility(int abilityId)
        {
            if (Count >= CAPACITY) return;
            AbilityIds[Count] = abilityId;
            TemplateIds[Count] = 0;
            TemplateWorldIds[Count] = 0;
            TemplateVersions[Count] = 0;
            Count++;
        }

        public void AddAbility(Entity templateEntity)
        {
            if (Count >= CAPACITY) return;
            AbilityIds[Count] = 0;
            TemplateIds[Count] = templateEntity.Id;
            TemplateWorldIds[Count] = templateEntity.WorldId;
            TemplateVersions[Count] = templateEntity.Version;
            Count++;
        }
        
        public AbilitySlotState Get(int index)
        {
            if (index < 0 || index >= Count) return default;
            return new AbilitySlotState 
            { 
                AbilityId = AbilityIds[index],
                TemplateEntityId = TemplateIds[index],
                TemplateEntityWorldId = TemplateWorldIds[index],
                TemplateEntityVersion = TemplateVersions[index]
            };
        }
    }

    /// <summary>
    /// Temporary ability overrides granted by items, buffs, or other effects.
    /// Each entry maps a slot index to an override ability. When resolved,
    /// a granted slot takes precedence over the base slot in <see cref="AbilityStateBuffer"/>.
    /// 
    /// Use <see cref="AbilitySlotResolver"/> to merge base + granted for the effective ability.
    /// </summary>
    public unsafe struct GrantedSlotBuffer
    {
        public const int CAPACITY = AbilityStateBuffer.CAPACITY;
        
        /// <summary>Ability ID overrides. 0 = no override for this slot.</summary>
        public fixed int AbilityIds[CAPACITY];
        /// <summary>Template entity overrides. 0 = no override for this slot.</summary>
        public fixed int TemplateIds[CAPACITY];
        public fixed int TemplateWorldIds[CAPACITY];
        public fixed int TemplateVersions[CAPACITY];
        /// <summary>Source tag ID that granted this override (used for removal when source expires).</summary>
        public fixed int SourceTagIds[CAPACITY];

        /// <summary>
        /// Grant an ability override to a specific slot.
        /// </summary>
        /// <param name="slotIndex">The slot index to override (0 to CAPACITY-1).</param>
        /// <param name="abilityId">The override ability ID.</param>
        /// <param name="sourceTagId">The tag ID that granted this override (for tracking removal).</param>
        public void Grant(int slotIndex, int abilityId, int sourceTagId)
        {
            if ((uint)slotIndex >= CAPACITY) return;
            AbilityIds[slotIndex] = abilityId;
            TemplateIds[slotIndex] = 0;
            TemplateWorldIds[slotIndex] = 0;
            TemplateVersions[slotIndex] = 0;
            SourceTagIds[slotIndex] = sourceTagId;
        }
        
        /// <summary>
        /// Grant an ability override using a template entity.
        /// </summary>
        public void Grant(int slotIndex, Entity templateEntity, int sourceTagId)
        {
            if ((uint)slotIndex >= CAPACITY) return;
            AbilityIds[slotIndex] = 0;
            TemplateIds[slotIndex] = templateEntity.Id;
            TemplateWorldIds[slotIndex] = templateEntity.WorldId;
            TemplateVersions[slotIndex] = templateEntity.Version;
            SourceTagIds[slotIndex] = sourceTagId;
        }
        
        /// <summary>
        /// Revoke an override from a specific slot.
        /// </summary>
        public void Revoke(int slotIndex)
        {
            if ((uint)slotIndex >= CAPACITY) return;
            AbilityIds[slotIndex] = 0;
            TemplateIds[slotIndex] = 0;
            TemplateWorldIds[slotIndex] = 0;
            TemplateVersions[slotIndex] = 0;
            SourceTagIds[slotIndex] = 0;
        }
        
        /// <summary>
        /// Revoke all overrides granted by a specific source tag.
        /// Used when a buff/item effect expires.
        /// </summary>
        /// <param name="sourceTagId">The source tag ID to match.</param>
        /// <returns>Number of slots revoked.</returns>
        public int RevokeBySource(int sourceTagId)
        {
            int count = 0;
            for (int i = 0; i < CAPACITY; i++)
            {
                if (SourceTagIds[i] == sourceTagId && (AbilityIds[i] != 0 || TemplateIds[i] != 0))
                {
                    Revoke(i);
                    count++;
                }
            }
            return count;
        }
        
        /// <summary>
        /// Check if a slot has an override.
        /// </summary>
        public bool HasOverride(int slotIndex)
        {
            if ((uint)slotIndex >= CAPACITY) return false;
            return AbilityIds[slotIndex] != 0 || TemplateIds[slotIndex] != 0;
        }
        
        /// <summary>
        /// Get the override for a slot.
        /// </summary>
        public AbilitySlotState GetOverride(int slotIndex)
        {
            if ((uint)slotIndex >= CAPACITY) return default;
            return new AbilitySlotState
            {
                AbilityId = AbilityIds[slotIndex],
                TemplateEntityId = TemplateIds[slotIndex],
                TemplateEntityWorldId = TemplateWorldIds[slotIndex],
                TemplateEntityVersion = TemplateVersions[slotIndex]
            };
        }
    }

    /// <summary>
    /// Utility for resolving the effective ability at a slot index
    /// by merging base slots + granted overrides.
    /// </summary>
    public static class AbilitySlotResolver
    {
        /// <summary>
        /// Resolve the effective ability for a slot.
        /// If the entity has a <see cref="GrantedSlotBuffer"/> and the slot has an override, use it.
        /// Otherwise, use the base slot from <see cref="AbilityStateBuffer"/>.
        /// </summary>
        public static AbilitySlotState Resolve(
            in AbilityStateBuffer baseSlots,
            in GrantedSlotBuffer grantedSlots,
            bool hasGranted,
            int slotIndex)
        {
            if (hasGranted && grantedSlots.HasOverride(slotIndex))
            {
                return grantedSlots.GetOverride(slotIndex);
            }
            return baseSlots.Get(slotIndex);
        }
    }
}
