using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;

namespace Ludots.Core.UI.EntityCommandPanels
{
    /// <summary>Merged root of <c>UI/ability_aggregation_profiles.json</c> (RFC-0065 PNL-1/2, DEC-10).</summary>
    public sealed class AbilityAggregationProfilesConfig
    {
        public List<AbilityAggregationProfileDefinition> Profiles { get; set; }
    }

    /// <summary>
    /// One panel aggregation profile. <c>GroupBy</c> is a key selector expression resolved by prefix
    /// through the registry's selector table (DEC-10, non-closed enum): <c>catalog.&lt;tagPrefix&gt;</c>,
    /// <c>template.id</c>, <c>ability.id</c>, plus any mod-registered prefix. <c>Overflow</c> is an
    /// opaque registry key consumed by the panel router (PNL-3); the kernel never interprets it.
    /// </summary>
    public sealed class AbilityAggregationProfileDefinition
    {
        public string Id { get; set; } = string.Empty;
        public string GroupBy { get; set; } = string.Empty;
        public string Overflow { get; set; }
    }

    /// <summary>
    /// Group key namespaces for <see cref="AbilityAggregationResult"/> keys. Keys are
    /// <c>(kind &lt;&lt; 32) | id</c> so groups sort deterministically by (kind, id).
    /// These are key layout constants, not a dispatch enum — grouping dispatch goes through
    /// the registry's selector delegate table (DEC-11).
    /// </summary>
    public static class AbilityAggregationKeyKinds
    {
        /// <summary>Key id is an ability catalog tag id (matched via <c>catalog.&lt;tagPrefix&gt;</c>).</summary>
        public const int CatalogTag = 1;

        /// <summary>Key id is the ability definition id (<see cref="Gameplay.GAS.Registry.AbilityIdRegistry"/> space).</summary>
        public const int AbilityId = 2;

        /// <summary>Key id is a template entity id (slot backed by a template entity without an ability id).</summary>
        public const int TemplateEntityId = 3;

        /// <summary>Key id is the owner unit template plus command slot index.</summary>
        public const int OwnerTemplateSlot = 4;

        /// <summary>Compose a group key from a kind namespace and an id.</summary>
        public static long MakeKey(int kind, int id)
        {
            return ((long)kind << 32) | (uint)id;
        }
    }

    /// <summary>
    /// Computes the group key for one effective (non-empty) ability slot. Returning the same key
    /// for two slots merges them into one panel group. Must be pure and allocation free.
    /// </summary>
    public readonly struct AbilityAggregationSlotContext
    {
        public AbilityAggregationSlotContext(Entity owner, int ownerTemplateKeyId, int slotIndex)
        {
            Owner = owner;
            OwnerTemplateKeyId = ownerTemplateKeyId;
            SlotIndex = slotIndex;
        }

        public Entity Owner { get; }
        public int OwnerTemplateKeyId { get; }
        public int SlotIndex { get; }
    }

    public delegate long AbilityAggregationKeySelector(
        in AbilitySlotState slot,
        in AbilityAggregationSlotContext context,
        AbilityDefinitionRegistry abilities);

    /// <summary>
    /// Compiles a <c>groupBy</c> expression into a key selector at profile install time.
    /// <paramref name="argument"/> is the expression remainder after the registered prefix and its
    /// separator dot (empty when the expression is exactly the prefix). Implementations must throw
    /// on invalid arguments (fail fast at load).
    /// </summary>
    public delegate AbilityAggregationKeySelector AbilityAggregationKeySelectorFactory(string argument);

    /// <summary>
    /// Reusable pooled SoA output of <see cref="AbilityAggregationProfileRegistry.BuildGroups"/>:
    /// group keys plus group boundaries over flat parallel (entity, slotIndex) member columns.
    /// Groups are ordered by key ascending; members within a group by entity id then slot index
    /// ascending. Buffers grow on demand and are reused across calls (steady-state 0 alloc).
    /// </summary>
    public sealed class AbilityAggregationResult
    {
        private long[] _groupKeys = new long[16];
        private int[] _groupStarts = new int[17];
        private Entity[] _memberEntities = new Entity[64];
        private int[] _memberSlotIndices = new int[64];
        private long[] _memberKeys = new long[64];
        private int _groupCount;
        private int _memberCount;

        /// <summary>Number of groups produced by the last build.</summary>
        public int GroupCount => _groupCount;

        /// <summary>Total number of (entity, slotIndex) members across all groups.</summary>
        public int MemberCount => _memberCount;

        /// <summary>Group key of <paramref name="groupIndex"/> (see <see cref="AbilityAggregationKeyKinds"/> layout).</summary>
        public long GetGroupKey(int groupIndex)
        {
            ValidateGroupIndex(groupIndex);
            return _groupKeys[groupIndex];
        }

        /// <summary>Member entities of <paramref name="groupIndex"/>, parallel to <see cref="GroupSlotIndices"/>.</summary>
        public ReadOnlySpan<Entity> GroupEntities(int groupIndex)
        {
            ValidateGroupIndex(groupIndex);
            return _memberEntities.AsSpan(_groupStarts[groupIndex], _groupStarts[groupIndex + 1] - _groupStarts[groupIndex]);
        }

        /// <summary>Effective ability slot indices of <paramref name="groupIndex"/>, parallel to <see cref="GroupEntities"/>.</summary>
        public ReadOnlySpan<int> GroupSlotIndices(int groupIndex)
        {
            ValidateGroupIndex(groupIndex);
            return _memberSlotIndices.AsSpan(_groupStarts[groupIndex], _groupStarts[groupIndex + 1] - _groupStarts[groupIndex]);
        }

        private void ValidateGroupIndex(int groupIndex)
        {
            if ((uint)groupIndex >= (uint)_groupCount)
            {
                throw new ArgumentOutOfRangeException(nameof(groupIndex), groupIndex, $"Result has {_groupCount} group(s).");
            }
        }

        internal void Reset()
        {
            _groupCount = 0;
            _memberCount = 0;
        }

        internal void AppendMember(long key, Entity entity, int slotIndex)
        {
            if (_memberCount == _memberEntities.Length)
            {
                int next = _memberEntities.Length * 2;
                Array.Resize(ref _memberEntities, next);
                Array.Resize(ref _memberSlotIndices, next);
                Array.Resize(ref _memberKeys, next);
            }

            _memberKeys[_memberCount] = key;
            _memberEntities[_memberCount] = entity;
            _memberSlotIndices[_memberCount] = slotIndex;
            _memberCount++;
        }

        /// <summary>
        /// Sort members by (key asc, entity id asc, slot asc) and emit group boundaries where the
        /// key changes. Insertion sort on the parallel columns: deterministic, in-place, 0 alloc
        /// (member counts are panel-scale).
        /// </summary>
        internal void SortAndSeal()
        {
            for (int i = 1; i < _memberCount; i++)
            {
                long key = _memberKeys[i];
                Entity entity = _memberEntities[i];
                int slot = _memberSlotIndices[i];
                int j = i - 1;
                while (j >= 0 && Compare(_memberKeys[j], _memberEntities[j], _memberSlotIndices[j], key, entity, slot) > 0)
                {
                    _memberKeys[j + 1] = _memberKeys[j];
                    _memberEntities[j + 1] = _memberEntities[j];
                    _memberSlotIndices[j + 1] = _memberSlotIndices[j];
                    j--;
                }

                _memberKeys[j + 1] = key;
                _memberEntities[j + 1] = entity;
                _memberSlotIndices[j + 1] = slot;
            }

            for (int i = 0; i < _memberCount; i++)
            {
                if (i == 0 || _memberKeys[i] != _memberKeys[i - 1])
                {
                    if (_groupCount == _groupKeys.Length)
                    {
                        Array.Resize(ref _groupKeys, _groupKeys.Length * 2);
                        Array.Resize(ref _groupStarts, _groupKeys.Length + 1);
                    }

                    _groupKeys[_groupCount] = _memberKeys[i];
                    _groupStarts[_groupCount] = i;
                    _groupCount++;
                }
            }

            _groupStarts[_groupCount] = _memberCount;
        }

        private static int Compare(long leftKey, Entity leftEntity, int leftSlot, long rightKey, Entity rightEntity, int rightSlot)
        {
            int keyCompare = leftKey.CompareTo(rightKey);
            if (keyCompare != 0)
            {
                return keyCompare;
            }

            int entityCompare = leftEntity.Id.CompareTo(rightEntity.Id);
            return entityCompare != 0 ? entityCompare : leftSlot.CompareTo(rightSlot);
        }
    }
}
