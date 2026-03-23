using System;
using System.Collections.Generic;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Spatial;

namespace Ludots.Core.Gameplay.GAS.Systems
{
    public sealed class ProjectileRuntimeSystem : BaseSystem<World, float>
    {
        private static readonly QueryDescription Query = new QueryDescription().WithAll<ProjectileState, WorldPositionCm>();
        private static readonly Fix64 Deg180 = Fix64.FromInt(180);

        private readonly EffectRequestQueue _effectRequests;
        private readonly ISpatialQueryService _spatialQueries;
        private readonly List<Entity> _toDestroy = new();

        public ProjectileRuntimeSystem(World world, EffectRequestQueue effectRequests, ISpatialQueryService spatialQueries) : base(world)
        {
            _effectRequests = effectRequests;
            _spatialQueries = spatialQueries;
        }

        public override void Update(in float dt)
        {
            if (_effectRequests == null)
            {
                return;
            }

            _toDestroy.Clear();
            Fix64 deltaTime = Fix64.FromFloat(dt);

            World.Query(in Query, (Entity entity, ref ProjectileState projectile, ref WorldPositionCm position) =>
            {
                UpdateProjectile(entity, ref projectile, ref position, deltaTime);
            });

            for (int i = 0; i < _toDestroy.Count; i++)
            {
                if (World.IsAlive(_toDestroy[i]))
                {
                    World.Destroy(_toDestroy[i]);
                }
            }
        }

        private void UpdateProjectile(Entity entity, ref ProjectileState projectile, ref WorldPositionCm position, Fix64 deltaTime)
        {
            if (!World.IsAlive(projectile.Source))
            {
                _toDestroy.Add(entity);
                return;
            }

            if (projectile.Speed <= Fix64.Zero || projectile.Range <= 0)
            {
                _toDestroy.Add(entity);
                return;
            }

            Fix64 stepBudgetCm = projectile.Speed * deltaTime;
            if (stepBudgetCm <= Fix64.Zero)
            {
                return;
            }

            Fix64Vec2 current = position.Value;
            bool legCompleted = false;

            if (projectile.FlightPhase == ProjectileFlightPhase.Outbound)
            {
                Fix64 remainingRangeCm = Fix64.FromInt(projectile.Range) - projectile.TraveledCm;
                if (remainingRangeCm <= Fix64.Zero)
                {
                    legCompleted = true;
                }
                else if (remainingRangeCm < stepBudgetCm)
                {
                    stepBudgetCm = remainingRangeCm;
                }
            }

            Fix64Vec2 next = current;
            Fix64 actualStepCm = stepBudgetCm;

            if (!legCompleted)
            {
                if (projectile.FlightPhase == ProjectileFlightPhase.Returning)
                {
                    legCompleted = TryMoveReturnToSource(ref projectile, current, stepBudgetCm, deltaTime, out next, out actualStepCm);
                }
                else
                {
                    switch (projectile.TravelMode)
                    {
                        case ProjectileTravelMode.Direction:
                            TryMoveDirection(ref projectile, current, stepBudgetCm, out next);
                            break;

                        case ProjectileTravelMode.TrackTarget:
                            legCompleted = TryMoveTrackTarget(ref projectile, current, stepBudgetCm, deltaTime, out next, out actualStepCm);
                            break;

                        default:
                            legCompleted = TryMoveLegacy(ref projectile, current, stepBudgetCm, out next, out actualStepCm);
                            break;
                    }
                }
            }

            if (actualStepCm > Fix64.Zero && next != current)
            {
                if (TryResolveTravelHits(entity, ref projectile, current, next, out Entity firstHit))
                {
                    if (projectile.ImpactPolicy == ProjectileImpactPolicy.DestroyOnFirstHit)
                    {
                        if (World.Has<WorldPositionCm>(firstHit))
                        {
                            position.Value = World.Get<WorldPositionCm>(firstHit).Value;
                        }

                        _toDestroy.Add(entity);
                        return;
                    }
                }

                if (projectile.FlightPhase == ProjectileFlightPhase.Outbound)
                {
                    projectile.TraveledCm += actualStepCm;
                }

                var delta = next - current;
                if (delta.LengthSquared() > Fix64.OneValue)
                {
                    projectile.Direction = delta.Normalized();
                    projectile.HasDirection = 1;
                }

                position.Value = next;
            }

            if (projectile.FlightPhase == ProjectileFlightPhase.Outbound &&
                projectile.TraveledCm >= Fix64.FromInt(projectile.Range))
            {
                legCompleted = true;
            }

            if (!legCompleted)
            {
                return;
            }

            if (projectile.FlightPhase == ProjectileFlightPhase.Outbound &&
                projectile.ReturnMode == ProjectileReturnMode.ReturnToSource)
            {
                BeginReturn(ref projectile, position.Value);
                return;
            }

            PublishEffect(projectile.ImpactEffectTemplateId, in projectile, World.IsAlive(projectile.Target) ? projectile.Target : Entity.Null, position.Value);
            _toDestroy.Add(entity);
        }

        private void BeginReturn(ref ProjectileState projectile, in Fix64Vec2 currentPosition)
        {
            projectile.FlightPhase = ProjectileFlightPhase.Returning;
            if (projectile.ResetHitHistoryOnReturn != 0)
            {
                projectile.ClearHitHistory();
            }

            if (World.IsAlive(projectile.Source) && World.Has<WorldPositionCm>(projectile.Source))
            {
                var sourcePosition = World.Get<WorldPositionCm>(projectile.Source).Value;
                var delta = sourcePosition - currentPosition;
                if (delta.LengthSquared() > Fix64.OneValue)
                {
                    projectile.Direction = delta.Normalized();
                    projectile.HasDirection = 1;
                }
            }
        }

        private bool TryResolveTravelHits(Entity projectileEntity, ref ProjectileState projectile, in Fix64Vec2 current, in Fix64Vec2 next, out Entity firstHit)
        {
            firstHit = Entity.Null;
            if (_spatialQueries == null ||
                projectile.HitEffectTemplateId <= 0 ||
                projectile.CollisionHalfWidthCm <= 0 ||
                projectile.ImpactPolicy == ProjectileImpactPolicy.Legacy)
            {
                return false;
            }

            var delta = next - current;
            Fix64 segmentLength = delta.Length();
            if (segmentLength <= Fix64.OneValue)
            {
                return false;
            }

            int lengthCm = segmentLength.RoundToInt();
            if (lengthCm <= 0)
            {
                return false;
            }

            int directionDeg = ComputeDirectionDeg(delta);
            Span<Entity> rawHits = stackalloc Entity[128];
            int hitCount = _spatialQueries.QueryLine(
                current.ToWorldCmInt2(),
                directionDeg,
                lengthCm,
                projectile.CollisionHalfWidthCm,
                rawHits).Count;
            if (hitCount <= 0)
            {
                return false;
            }

            Span<Entity> orderedHits = stackalloc Entity[32];
            Span<int> projections = stackalloc int[32];
            int orderedCount = 0;
            int sourceTeamId = TryGetTeamId(projectile.Source);

            for (int i = 0; i < hitCount; i++)
            {
                Entity candidate = rawHits[i];
                if (!IsValidCollisionTarget(projectileEntity, in projectile, candidate, sourceTeamId))
                {
                    continue;
                }

                if (!World.Has<WorldPositionCm>(candidate))
                {
                    continue;
                }

                var candidatePos = World.Get<WorldPositionCm>(candidate).Value;
                int projection = ComputeSegmentProjectionCm(current, next, candidatePos);
                if (projection < 0)
                {
                    continue;
                }

                if (orderedCount >= orderedHits.Length)
                {
                    break;
                }

                int insertAt = orderedCount;
                while (insertAt > 0 && projection < projections[insertAt - 1])
                {
                    projections[insertAt] = projections[insertAt - 1];
                    orderedHits[insertAt] = orderedHits[insertAt - 1];
                    insertAt--;
                }

                projections[insertAt] = projection;
                orderedHits[insertAt] = candidate;
                orderedCount++;
            }

            if (orderedCount == 0)
            {
                return false;
            }

            for (int i = 0; i < orderedCount; i++)
            {
                Entity candidate = orderedHits[i];
                if (!projectile.TryRecordHit(candidate))
                {
                    continue;
                }

                var impactPosition = World.Has<WorldPositionCm>(candidate)
                    ? World.Get<WorldPositionCm>(candidate).Value
                    : next;
                PublishEffect(projectile.HitEffectTemplateId, in projectile, candidate, impactPosition);

                if (firstHit == Entity.Null)
                {
                    firstHit = candidate;
                }

                if (projectile.MaxHitCount > 0 && projectile.DistinctHitCount >= projectile.MaxHitCount)
                {
                    _toDestroy.Add(projectileEntity);
                    return true;
                }

                if (projectile.ImpactPolicy == ProjectileImpactPolicy.DestroyOnFirstHit)
                {
                    return true;
                }
            }

            return firstHit != Entity.Null;
        }

        private bool IsValidCollisionTarget(Entity projectileEntity, in ProjectileState projectile, Entity candidate, int sourceTeamId)
        {
            if (!World.IsAlive(candidate) || candidate == projectileEntity)
            {
                return false;
            }

            if (projectile.CollisionExcludeSource != 0 && candidate.Equals(projectile.Source))
            {
                return false;
            }

            if (projectile.HasRecordedHit(candidate))
            {
                return false;
            }

            if (projectile.CollisionRelationFilter == RelationshipFilter.All)
            {
                return true;
            }

            if (sourceTeamId == 0 || !World.Has<Team>(candidate))
            {
                return false;
            }

            int targetTeamId = World.Get<Team>(candidate).Id;
            return RelationshipFilterUtil.Passes(projectile.CollisionRelationFilter, sourceTeamId, targetTeamId);
        }

        private int TryGetTeamId(Entity entity)
        {
            return World.IsAlive(entity) && World.Has<Team>(entity)
                ? World.Get<Team>(entity).Id
                : 0;
        }

        private void PublishEffect(int templateId, in ProjectileState projectile, Entity target, in Fix64Vec2 impactPosition)
        {
            if (templateId <= 0)
            {
                return;
            }

            var request = new EffectRequest
            {
                RootId = 0,
                Source = projectile.Source,
                Target = target,
                TargetContext = Entity.Null,
                TemplateId = templateId,
            };

            var callerParams = new EffectConfigParams();
            bool hasCallerParams = false;

            if (projectile.HasLaunchOrigin != 0)
            {
                hasCallerParams |= callerParams.TryAddFloat(EffectParamKeys.TargetOriginX, projectile.LaunchOriginCm.X.ToFloat());
                hasCallerParams |= callerParams.TryAddFloat(EffectParamKeys.TargetOriginY, projectile.LaunchOriginCm.Y.ToFloat());
            }

            hasCallerParams |= callerParams.TryAddFloat(EffectParamKeys.TargetPosX, impactPosition.X.ToFloat());
            hasCallerParams |= callerParams.TryAddFloat(EffectParamKeys.TargetPosY, impactPosition.Y.ToFloat());

            request.CallerParams = callerParams;
            request.HasCallerParams = hasCallerParams;
            _effectRequests.Publish(request);
        }

        private static bool TryMoveDirection(ref ProjectileState projectile, in Fix64Vec2 current, Fix64 stepBudgetCm, out Fix64Vec2 next)
        {
            if (projectile.HasDirection == 0)
            {
                next = current;
                return false;
            }

            next = current + projectile.Direction * stepBudgetCm;
            return true;
        }

        private bool TryMoveTrackTarget(ref ProjectileState projectile, in Fix64Vec2 current, Fix64 stepBudgetCm, Fix64 deltaTime, out Fix64Vec2 next, out Fix64 actualStepCm)
        {
            actualStepCm = stepBudgetCm;
            if (World.IsAlive(projectile.Target) && World.Has<WorldPositionCm>(projectile.Target))
            {
                var targetPosition = World.Get<WorldPositionCm>(projectile.Target).Value;
                return TryMoveTowardPoint(ref projectile, current, targetPosition, stepBudgetCm, deltaTime, out next, out actualStepCm);
            }

            next = current;
            actualStepCm = Fix64.Zero;
            return false;
        }

        private bool TryMoveReturnToSource(ref ProjectileState projectile, in Fix64Vec2 current, Fix64 stepBudgetCm, Fix64 deltaTime, out Fix64Vec2 next, out Fix64 actualStepCm)
        {
            actualStepCm = stepBudgetCm;
            if (World.IsAlive(projectile.Source) && World.Has<WorldPositionCm>(projectile.Source))
            {
                var sourcePosition = World.Get<WorldPositionCm>(projectile.Source).Value;
                return TryMoveTowardPoint(ref projectile, current, sourcePosition, stepBudgetCm, deltaTime, out next, out actualStepCm);
            }

            next = current;
            actualStepCm = Fix64.Zero;
            return false;
        }

        private bool TryMoveLegacy(ref ProjectileState projectile, in Fix64Vec2 current, Fix64 stepBudgetCm, out Fix64Vec2 next, out Fix64 actualStepCm)
        {
            actualStepCm = stepBudgetCm;

            if (World.IsAlive(projectile.Target) && World.Has<WorldPositionCm>(projectile.Target))
            {
                var targetPosition = World.Get<WorldPositionCm>(projectile.Target).Value;
                return TryMoveTowardPoint(ref projectile, current, targetPosition, stepBudgetCm, Fix64.Zero, out next, out actualStepCm);
            }

            if (projectile.HasTargetPoint != 0)
            {
                return TryMoveTowardPoint(ref projectile, current, projectile.TargetPointCm, stepBudgetCm, Fix64.Zero, out next, out actualStepCm);
            }

            if (projectile.HasDirection != 0)
            {
                next = current + projectile.Direction * stepBudgetCm;
                return false;
            }

            next = current + new Fix64Vec2(stepBudgetCm, Fix64.Zero);
            return false;
        }

        private bool TryMoveTowardPoint(
            ref ProjectileState projectile,
            in Fix64Vec2 current,
            in Fix64Vec2 destination,
            Fix64 stepBudgetCm,
            Fix64 deltaTime,
            out Fix64Vec2 next,
            out Fix64 actualStepCm)
        {
            var delta = destination - current;
            Fix64 distance = delta.Length();
            if (distance <= stepBudgetCm || distance <= Fix64.OneValue)
            {
                next = destination;
                actualStepCm = distance;
                if (distance > Fix64.OneValue)
                {
                    projectile.Direction = delta.Normalized();
                    projectile.HasDirection = 1;
                }

                return true;
            }

            var desiredDirection = delta.Normalized();
            var travelDirection = ResolveTravelDirection(ref projectile, desiredDirection, deltaTime);
            next = current + travelDirection * stepBudgetCm;
            actualStepCm = stepBudgetCm;
            return false;
        }

        private static Fix64Vec2 ResolveTravelDirection(ref ProjectileState projectile, in Fix64Vec2 desiredDirection, Fix64 deltaTime)
        {
            Fix64Vec2 direction = desiredDirection;
            if (projectile.TurnRateDegPerSecond > 0 &&
                projectile.HasDirection != 0 &&
                deltaTime > Fix64.Zero)
            {
                Fix64 maxTurnRad = Fix64.FromInt(projectile.TurnRateDegPerSecond) * deltaTime * Fix64.Deg2Rad;
                Fix64 currentRad = Fix64Math.Atan2Fast(projectile.Direction.Y, projectile.Direction.X);
                Fix64 desiredRad = Fix64Math.Atan2Fast(desiredDirection.Y, desiredDirection.X);
                Fix64 deltaRad = NormalizeRadians(desiredRad - currentRad);
                if (Fix64.Abs(deltaRad) > maxTurnRad)
                {
                    desiredRad = currentRad + (deltaRad > Fix64.Zero ? maxTurnRad : -maxTurnRad);
                    direction = new Fix64Vec2(Fix64Math.Cos(desiredRad), Fix64Math.Sin(desiredRad));
                }
            }

            projectile.Direction = direction;
            projectile.HasDirection = 1;
            return direction;
        }

        private static Fix64 NormalizeRadians(Fix64 radians)
        {
            while (radians > Fix64.Pi)
            {
                radians -= Fix64.TwoPi;
            }

            while (radians < -Fix64.Pi)
            {
                radians += Fix64.TwoPi;
            }

            return radians;
        }

        private static int ComputeDirectionDeg(in Fix64Vec2 delta)
        {
            var radians = Fix64Math.Atan2Fast(delta.Y, delta.X);
            int degrees = (radians * Deg180 / Fix64.Pi).RoundToInt();
            if (degrees < 0)
            {
                degrees += 360;
            }

            return degrees;
        }

        private static int ComputeSegmentProjectionCm(in Fix64Vec2 start, in Fix64Vec2 end, in Fix64Vec2 point)
        {
            var segment = end - start;
            var segmentLengthSq = segment.LengthSquared();
            if (segmentLengthSq <= Fix64.OneValue)
            {
                return -1;
            }

            var offset = point - start;
            Fix64 projection = (offset.X * segment.X + offset.Y * segment.Y) / segmentLengthSq;
            if (projection < Fix64.Zero || projection > Fix64.OneValue)
            {
                return -1;
            }

            return (segment.Length() * projection).RoundToInt();
        }
    }
}
