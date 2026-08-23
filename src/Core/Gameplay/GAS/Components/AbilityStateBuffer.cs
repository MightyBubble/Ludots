using System;
using Arch.Core;
using Ludots.Core.Gameplay.Items;

namespace Ludots.Core.Gameplay.GAS.Components
{
    public struct AbilitySlotState
    {
        public int AbilityId;
        public int TemplateEntityId;
        public int TemplateEntityWorldId;
        public int TemplateEntityVersion;

        public readonly bool IsConfigured => AbilityId > 0 || TemplateEntityId > 0;
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
    /// Form-driven slot overrides resolved from actor state tags.
    /// This is separate from <see cref="GrantedSlotBuffer"/> so stance/form routing
    /// does not collide with transient grants from items, buffs, or effects.
    /// </summary>
    public unsafe struct AbilityFormSlotBuffer
    {
        public const int CAPACITY = AbilityStateBuffer.CAPACITY;

        public fixed int AbilityIds[CAPACITY];
        public fixed int TemplateIds[CAPACITY];
        public fixed int TemplateWorldIds[CAPACITY];
        public fixed int TemplateVersions[CAPACITY];

        public void SetOverride(int slotIndex, int abilityId)
        {
            if ((uint)slotIndex >= CAPACITY) return;
            AbilityIds[slotIndex] = abilityId;
            TemplateIds[slotIndex] = 0;
            TemplateWorldIds[slotIndex] = 0;
            TemplateVersions[slotIndex] = 0;
        }

        public void SetOverride(int slotIndex, Entity templateEntity)
        {
            if ((uint)slotIndex >= CAPACITY) return;
            AbilityIds[slotIndex] = 0;
            TemplateIds[slotIndex] = templateEntity.Id;
            TemplateWorldIds[slotIndex] = templateEntity.WorldId;
            TemplateVersions[slotIndex] = templateEntity.Version;
        }

        public void Clear(int slotIndex)
        {
            if ((uint)slotIndex >= CAPACITY) return;
            AbilityIds[slotIndex] = 0;
            TemplateIds[slotIndex] = 0;
            TemplateWorldIds[slotIndex] = 0;
            TemplateVersions[slotIndex] = 0;
        }

        public void ClearAll()
        {
            for (int i = 0; i < CAPACITY; i++)
            {
                Clear(i);
            }
        }

        public bool HasOverride(int slotIndex)
        {
            if ((uint)slotIndex >= CAPACITY) return false;
            return AbilityIds[slotIndex] != 0 || TemplateIds[slotIndex] != 0;
        }

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
    /// Utility for resolving the effective ability at a slot index by merging
    /// transient granted, item-granted, form, and base sources.
    /// </summary>
    public static class AbilitySlotResolver
    {
        /// <summary>
        /// Try to resolve the effective ability for a slot with layered overrides:
        /// transient granted override &gt; item-granted override &gt; form override &gt; base slot.
        /// </summary>
        public static bool TryResolve(
            in AbilityStateBuffer baseSlots,
            in AbilityFormSlotBuffer formSlots,
            bool hasForm,
            in ItemGrantedSlotBuffer itemGrantedSlots,
            bool hasItemGranted,
            in GrantedSlotBuffer grantedSlots,
            bool hasGranted,
            int slotIndex,
            out AbilitySlotState slot)
        {
            if ((uint)slotIndex >= AbilityStateBuffer.CAPACITY)
            {
                slot = default;
                return false;
            }

            if (hasGranted && grantedSlots.HasOverride(slotIndex))
            {
                slot = grantedSlots.GetOverride(slotIndex);
                return slot.IsConfigured;
            }

            if (hasItemGranted && itemGrantedSlots.HasOverride(slotIndex))
            {
                slot = itemGrantedSlots.GetOverride(slotIndex);
                return slot.IsConfigured;
            }

            if (hasForm && formSlots.HasOverride(slotIndex))
            {
                slot = formSlots.GetOverride(slotIndex);
                return slot.IsConfigured;
            }

            slot = baseSlots.Get(slotIndex);
            return slot.IsConfigured;
        }

        public static AbilitySlotState Resolve(
            in AbilityStateBuffer baseSlots,
            in AbilityFormSlotBuffer formSlots,
            bool hasForm,
            in ItemGrantedSlotBuffer itemGrantedSlots,
            bool hasItemGranted,
            in GrantedSlotBuffer grantedSlots,
            bool hasGranted,
            int slotIndex)
        {
            if (!TryResolve(
                    in baseSlots,
                    in formSlots,
                    hasForm,
                    in itemGrantedSlots,
                    hasItemGranted,
                    in grantedSlots,
                    hasGranted,
                    slotIndex,
                    out AbilitySlotState slot))
            {
                throw new InvalidOperationException(
                    $"Ability slot {slotIndex} is outside 0..{AbilityStateBuffer.CAPACITY - 1} or has no configured ability.");
            }

            return slot;
        }

        public static bool TryResolve(World world, Entity actor, int slotIndex, out AbilitySlotState slot)
        {
            slot = default;
            if (!world.IsAlive(actor) ||
                !world.Has<AbilityStateBuffer>(actor) ||
                (uint)slotIndex >= AbilityStateBuffer.CAPACITY)
            {
                return false;
            }

            ref AbilityStateBuffer baseSlots = ref world.Get<AbilityStateBuffer>(actor);
            bool hasForm = world.Has<AbilityFormSlotBuffer>(actor);
            AbilityFormSlotBuffer formSlots = hasForm ? world.Get<AbilityFormSlotBuffer>(actor) : default;
            bool hasItemGranted = world.Has<ItemGrantedSlotBuffer>(actor);
            ItemGrantedSlotBuffer itemGrantedSlots = hasItemGranted ? world.Get<ItemGrantedSlotBuffer>(actor) : default;
            bool hasGranted = world.Has<GrantedSlotBuffer>(actor);
            GrantedSlotBuffer grantedSlots = hasGranted ? world.Get<GrantedSlotBuffer>(actor) : default;
            return TryResolve(
                in baseSlots,
                in formSlots,
                hasForm,
                in itemGrantedSlots,
                hasItemGranted,
                in grantedSlots,
                hasGranted,
                slotIndex,
                out slot);
        }

        public static bool TryFindAbility(World world, Entity actor, int abilityId, out int slotIndex)
        {
            slotIndex = -1;
            if (abilityId <= 0 || !world.IsAlive(actor) || !world.Has<AbilityStateBuffer>(actor))
            {
                return false;
            }

            for (int i = 0; i < AbilityStateBuffer.CAPACITY; i++)
            {
                if (TryResolve(world, actor, i, out AbilitySlotState slot) && slot.AbilityId == abilityId)
                {
                    slotIndex = i;
                    return true;
                }
            }

            return false;
        }
    }
}
