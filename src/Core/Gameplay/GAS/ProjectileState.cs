using Arch.Core;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Gameplay.Teams;

namespace Ludots.Core.Gameplay.GAS
{
    public enum ProjectileFlightPhase : byte
    {
        Outbound = 0,
        Returning = 1,
    }

    public unsafe struct ProjectileState
    {
        public const int HitHistoryCapacity = 32;

        public Fix64 Speed;
        public int Range;
        public int ArcHeight;
        public int ImpactEffectTemplateId;
        public int HitEffectTemplateId;
        public int PresentationEffectTemplateId;
        public ProjectileTravelMode TravelMode;
        public ProjectileImpactPolicy ImpactPolicy;
        public int CollisionHalfWidthCm;
        public RelationshipFilter CollisionRelationFilter;
        public byte CollisionExcludeSource;
        public int MaxHitCount;
        public int TurnRateDegPerSecond;
        public ProjectileReturnMode ReturnMode;
        public byte ResetHitHistoryOnReturn;
        public ProjectileFlightPhase FlightPhase;
        public Entity Source;
        public Entity Target;
        public Fix64Vec2 LaunchOriginCm;
        public byte HasLaunchOrigin;
        public Fix64Vec2 TargetPointCm;
        public byte HasTargetPoint;
        public Fix64Vec2 Direction;
        public byte HasDirection;
        public Fix64 TraveledCm;
        public int DistinctHitCount;
        public fixed int HitIds[HitHistoryCapacity];
        public fixed int HitWorldIds[HitHistoryCapacity];
        public fixed int HitVersions[HitHistoryCapacity];

        public bool HasRecordedHit(Entity entity)
        {
            for (int i = 0; i < DistinctHitCount; i++)
            {
                if (HitIds[i] == entity.Id &&
                    HitWorldIds[i] == entity.WorldId &&
                    HitVersions[i] == entity.Version)
                {
                    return true;
                }
            }

            return false;
        }

        public bool TryRecordHit(Entity entity)
        {
            if (HasRecordedHit(entity))
            {
                return true;
            }

            if (DistinctHitCount >= HitHistoryCapacity)
            {
                return false;
            }

            HitIds[DistinctHitCount] = entity.Id;
            HitWorldIds[DistinctHitCount] = entity.WorldId;
            HitVersions[DistinctHitCount] = entity.Version;
            DistinctHitCount++;
            return true;
        }

        public void ClearHitHistory()
        {
            DistinctHitCount = 0;
        }
    }
}
