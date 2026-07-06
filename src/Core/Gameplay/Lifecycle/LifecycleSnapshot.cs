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
                hasStableId);
        }

        public static LifecycleSnapshot CaptureDeployConsumeSource(World world, Entity source) => Capture(world, source);

        private LifecycleSnapshot(
            bool hasPlayerOwner,
            PlayerOwner playerOwner,
            bool hasTeam,
            Team team,
            AttributeBuffer attributes,
            bool hasAttributes,
            int stableId,
            bool hasStableId)
        {
            HasPlayerOwner = hasPlayerOwner;
            PlayerOwner = playerOwner;
            HasTeam = hasTeam;
            Team = team;
            Attributes = attributes;
            HasAttributes = hasAttributes;
            StableId = stableId;
            HasStableId = hasStableId;
        }
    }
}
