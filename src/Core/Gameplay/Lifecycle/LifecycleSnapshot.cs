using Arch.Core;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Presentation.Components;

namespace Ludots.Core.Gameplay.Lifecycle
{
    public readonly struct LifecycleSnapshot
    {
        public readonly bool HasPlayerOwner;
        public readonly PlayerOwner PlayerOwner;
        public readonly bool HasTeam;
        public readonly Team Team;
        public readonly AttributeBuffer Attributes;
        public readonly bool HasAttributes;
        public readonly int StableId;
        public readonly bool HasStableId;

        public static LifecycleSnapshot Capture(World world, Entity source)
        {
            bool hasPlayerOwner = world.Has<PlayerOwner>(source);
            bool hasTeam = world.Has<Team>(source);
            bool hasAttributes = world.Has<AttributeBuffer>(source);
            bool hasStableId = world.Has<PresentationStableId>(source);

            return new LifecycleSnapshot(
                hasPlayerOwner,
                hasPlayerOwner ? world.Get<PlayerOwner>(source) : default,
                hasTeam,
                hasTeam ? world.Get<Team>(source) : default,
                hasAttributes ? world.Get<AttributeBuffer>(source) : default,
                hasAttributes,
                hasStableId ? world.Get<PresentationStableId>(source).Value : 0,
                hasStableId,
                CaptureHighRow(world, source));
        }

        public static LifecycleSnapshot CaptureDeployConsumeSource(World world, Entity source) => Capture(world, source);

        /// <summary>高槽位（≥64）整行随档捕获（RFC-0067 P1）；无列存/无行时为 null。</summary>
        public readonly float[]? HighAttributes;

        private static float[]? CaptureHighRow(World world, Entity source)
        {
            var store = Ludots.Core.Gameplay.GAS.WorldAttributeStoreAmbient.Current;
            if (store == null || store.SlotCount <= AttributeBuffer.MAX_ATTRS || !store.TryGetRow(source, out int row))
            {
                return null;
            }

            int slots = store.SlotCount - AttributeBuffer.MAX_ATTRS;
            var values = new float[slots];
            for (int i = 0; i < slots; i++)
            {
                values[i] = store.GetCurrent(row, AttributeBuffer.MAX_ATTRS + i);
            }

            return values;
        }

        /// <summary>恢复期把高槽位行写回列存（实体换壳后由部署路径调用）。</summary>
        public void RestoreHighRow(Ludots.Core.Gameplay.GAS.WorldAttributeStore store, Entity target)
        {
            if (HighAttributes == null || HighAttributes.Length == 0)
            {
                return;
            }

            int row = store.EnsureRow(target);
            for (int i = 0; i < HighAttributes.Length && AttributeBuffer.MAX_ATTRS + i < store.SlotCount; i++)
            {
                store.SetCurrentRaw(row, AttributeBuffer.MAX_ATTRS + i, HighAttributes[i]);
            }
        }

        private LifecycleSnapshot(
            bool hasPlayerOwner,
            PlayerOwner playerOwner,
            bool hasTeam,
            Team team,
            AttributeBuffer attributes,
            bool hasAttributes,
            int stableId,
            bool hasStableId,
            float[]? highAttributes)
        {
            HasPlayerOwner = hasPlayerOwner;
            PlayerOwner = playerOwner;
            HasTeam = hasTeam;
            Team = team;
            Attributes = attributes;
            HasAttributes = hasAttributes;
            StableId = stableId;
            HasStableId = hasStableId;
            HighAttributes = highAttributes;
        }
    }
}
