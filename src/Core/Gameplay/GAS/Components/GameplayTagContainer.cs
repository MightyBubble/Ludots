using System;
using System.Runtime.CompilerServices;
using Arch.Core;
using Ludots.Core.Gameplay.GAS.Capacity;

namespace Ludots.Core.Gameplay.GAS.Components
{
    /// <summary>
    /// Entity-side handle into session <see cref="GasWorldColumnStore"/> tag bit columns.
    /// Prefer sharing <see cref="RowId"/> with <see cref="AttributeBuffer"/> on the same entity.
    /// </summary>
    public struct GameplayTagContainer
    {
        public const int InvalidRow = 0;

        /// <summary>
        /// Obsolete bridge for call sites still using the old 255 usable-id constant.
        /// Prefer <see cref="GasLoadTimeCapacitySession.Plan"/>.MaxUsableTagId for loops.
        /// </summary>
        public const int MAX_TAG_ID = GasLoadTimeCapacityPlan.AbsoluteMaxTagIdSpace - 1;

        public int RowId;

        public static GameplayTagContainer CreateAttached()
        {
            var store = GasLoadTimeCapacitySession.ActiveStore;
            return new GameplayTagContainer { RowId = store.AllocateEntityRow() };
        }

        public static GameplayTagContainer CreateAttachedShared(int rowId)
        {
            if (rowId == InvalidRow)
            {
                throw new ArgumentOutOfRangeException(nameof(rowId), "Cannot share an invalid gas world row.");
            }

            GasLoadTimeCapacitySession.ActiveStore.RetainEntityRow(rowId);
            return new GameplayTagContainer { RowId = rowId };
        }

        public static GameplayTagContainer CreateAttached(World world, Entity entity)
        {
            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }

            if (world.IsAlive(entity) &&
                world.Has<AttributeBuffer>(entity))
            {
                ref var attrs = ref world.Get<AttributeBuffer>(entity);
                if (attrs.RowId != AttributeBuffer.InvalidRow)
                {
                    return CreateAttachedShared(attrs.RowId);
                }
            }

            return CreateAttached();
        }

        public static void Release(ref GameplayTagContainer container)
        {
            if (container.RowId == InvalidRow)
            {
                return;
            }

            GasLoadTimeCapacitySession.ActiveStore.ReleaseEntityRow(container.RowId);
            container.RowId = InvalidRow;
        }

        public bool IsEmpty
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => GasLoadTimeCapacitySession.ActiveStoreUnchecked.AreTagsEmpty(RequireRow());
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AddTag(int tagId)
        {
            GasLoadTimeCapacitySession.ActiveStoreUnchecked.AddTag(RequireRow(), tagId);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RemoveTag(int tagId)
        {
            GasLoadTimeCapacitySession.ActiveStoreUnchecked.RemoveTag(RequireRow(), tagId);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RemoveTagRange(int startTagId, int endTagId)
        {
            GasLoadTimeCapacitySession.ActiveStoreUnchecked.RemoveTagRange(RequireRow(), startTagId, endTagId);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool HasTag(int tagId)
        {
            return GasLoadTimeCapacitySession.ActiveStoreUnchecked.HasTag(RequireRow(), tagId);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool HasAnyTagInRange(int startTagId, int endTagId)
        {
            return GasLoadTimeCapacitySession.ActiveStoreUnchecked.HasAnyTagInRange(RequireRow(), startTagId, endTagId);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool ContainsAll(in GameplayTagBitSet required)
        {
            return GasLoadTimeCapacitySession.ActiveStoreUnchecked.ContainsAllTags(RequireRow(), in required);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Intersects(in GameplayTagBitSet other)
        {
            return GasLoadTimeCapacitySession.ActiveStoreUnchecked.IntersectsTags(RequireRow(), in other);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Intersects(in GameplayTagContainer other)
        {
            return GasLoadTimeCapacitySession.ActiveStoreUnchecked.IntersectsTags(
                RequireRow(),
                other.RequireRow());
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int FirstCommonTag(in GameplayTagBitSet other)
        {
            return GasLoadTimeCapacitySession.ActiveStoreUnchecked.FirstCommonTag(RequireRow(), in other);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int CountCommonTags(in GameplayTagBitSet other)
        {
            return GasLoadTimeCapacitySession.ActiveStoreUnchecked.CountCommonTags(RequireRow(), in other);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Clear()
        {
            GasLoadTimeCapacitySession.ActiveStoreUnchecked.ClearTags(RequireRow());
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void CopyWordsTo(Span<ulong> destination)
        {
            GasLoadTimeCapacitySession.ActiveStoreUnchecked.CopyTagWordsTo(RequireRow(), destination);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void CopyWordsFrom(ReadOnlySpan<ulong> source)
        {
            GasLoadTimeCapacitySession.ActiveStoreUnchecked.CopyTagWordsFrom(RequireRow(), source);
        }

        public int WordCount
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => GasLoadTimeCapacitySession.Plan.TagUlongWordCount;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal int RequireRow()
        {
            if (RowId == InvalidRow)
            {
                ThrowDetached();
            }

            return RowId;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ThrowDetached()
        {
            throw new InvalidOperationException(
                "GameplayTagContainer has no world-store row. Use GameplayTagContainer.CreateAttached() or GasTagRows.Attach.");
        }
    }

    public static class GasTagRows
    {
        public static ref GameplayTagContainer Attach(World world, Entity entity)
        {
            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }

            if (!world.IsAlive(entity))
            {
                throw new InvalidOperationException("Cannot attach tag row to a dead entity.");
            }

            if (world.Has<GameplayTagContainer>(entity))
            {
                ref var existing = ref world.Get<GameplayTagContainer>(entity);
                if (existing.RowId != GameplayTagContainer.InvalidRow)
                {
                    return ref existing;
                }

                existing = GameplayTagContainer.CreateAttached(world, entity);
                return ref existing;
            }

            world.Add(entity, GameplayTagContainer.CreateAttached(world, entity));
            return ref world.Get<GameplayTagContainer>(entity);
        }

        public static void ReleaseIfPresent(World world, Entity entity)
        {
            if (world == null || !world.IsAlive(entity) || !world.Has<GameplayTagContainer>(entity))
            {
                return;
            }

            ref var container = ref world.Get<GameplayTagContainer>(entity);
            GameplayTagContainer.Release(ref container);
        }
    }

    public static class GasWorldRows
    {
        /// <summary>
        /// Releases the shared world row once when attribute and/or tag handles are present.
        /// Prefers refcount Release on each handle so shared rows free at zero.
        /// </summary>
        public static void ReleaseIfPresent(World world, Entity entity)
        {
            GasAttributeRows.ReleaseIfPresent(world, entity);
            GasTagRows.ReleaseIfPresent(world, entity);
        }
    }
}
