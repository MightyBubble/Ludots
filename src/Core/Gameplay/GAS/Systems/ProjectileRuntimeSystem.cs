using System;
using System.Collections.Generic;
using Arch.Buffer;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Presentation.Components;
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
        private readonly HashSet<Entity> _toDestroySet = new();
        private readonly CommandBuffer _commandBuffer = new();

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
            _toDestroySet.Clear();
            Fix64 deltaTime = Fix64.FromFloat(dt);

            foreach (ref var chunk in World.Query(in Query))
            {
                ref Entity entityFirst = ref chunk.Entity(0);
                var projectiles = chunk.GetSpan<ProjectileState>();
                var positions = chunk.GetSpan<WorldPositionCm>();
                foreach (var index in chunk)
                {
                    Entity entity = System.Runtime.CompilerServices.Unsafe.Add(ref entityFirst, index);
                    UpdateProjectile(entity, ref projectiles[index], ref positions[index], deltaTime);
                }
            }

            for (int i = 0; i < _toDestroy.Count; i++)
            {
                if (World.IsAlive(_toDestroy[i]))
                {
                    if (World.Has<PresentationStableId>(_toDestroy[i]))
                    {
                        if (!World.Has<PresentationDestroyPending>(_toDestroy[i]))
                        {
                            _commandBuffer.Add(_toDestroy[i], new PresentationDestroyPending());
                        }

                        if (World.Has<PresentationDestroyEventPublished>(_toDestroy[i]))
                        {
                            _commandBuffer.Remove<PresentationDestroyEventPublished>(_toDestroy[i]);
                        }

                        continue;
                    }

                    _commandBuffer.Destroy(_toDestroy[i]);
                }
            }

            if (_commandBuffer.Size > 0)
            {
                _commandBuffer.Playback(World);
            }
        }

        private void UpdateProjectile(Entity entity, ref ProjectileState projectile, ref WorldPositionCm position, Fix64 deltaTime)
        {
            if (!World.IsAlive(projectile.Source))
            {
                QueueDestroy(entity);
                return;
            }

            if (projectile.Speed <= Fix64.Zero || projectile.Range <= 0)
            {
                QueueDestroy(entity);
                return;
            }

            Fix64 stepBudgetCm = projectile.Speed * deltaTime;
            if (stepBudgetCm <= Fix64.Zero)
            {
                return;
            }

            Fix64Vec2 current = position.Value;
            bool completed = false;

            Fix64 remainingRangeCm = Fix64.FromInt(projectile.Range) - projectile.TraveledCm;
            if (remainingRangeCm <= Fix64.Zero)
            {
                completed = true;
            }
            else if (remainingRangeCm < stepBudgetCm)
            {
                stepBudgetCm = remainingRangeCm;
            }

            Fix64Vec2 next = current;
            Fix64 actualStepCm = stepBudgetCm;

            if (!completed)
            {
                switch (projectile.TravelMode)
                {
                    case ProjectileTravelMode.Direction:
                        if (!TryMoveDirection(in projectile, current, stepBudgetCm, out next))
                        {
                            next = current + new Fix64Vec2(stepBudgetCm, Fix64.Zero);
                        }
                        break;

                    case ProjectileTravelMode.TrackTarget:
                        completed = !TryMoveTrackTarget(in projectile, current, stepBudgetCm, out next, out actualStepCm);
                        break;

                    default:
                        completed = TryMoveLegacy(in projectile, current, stepBudgetCm, out next, out actualStepCm);
                        break;
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

                        QueueDestroy(entity);
                        return;
                    }
                }

                projectile.TraveledCm += actualStepCm;
                position.Value = next;
            }

            if (projectile.TraveledCm >= Fix64.FromInt(projectile.Range))
            {
                completed = true;
            }

            if (completed)
            {
                PublishEffect(projectile.ImpactEffectTemplateId, in projectile, World.IsAlive(projectile.Target) ? projectile.Target : Entity.Null, position.Value);
                QueueDestroy(entity);
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
                    QueueDestroy(projectileEntity);
                    return true;
                }

                if (projectile.ImpactPolicy == ProjectileImpactPolicy.DestroyOnFirstHit)
                {
                    return true;
                }
            }

            return firstHit != Entity.Null;
        }

        private void QueueDestroy(Entity entity)
        {
            if (_toDestroySet.Add(entity))
            {
                _toDestroy.Add(entity);
            }
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

        private static bool TryMoveDirection(in ProjectileState projectile, in Fix64Vec2 current, Fix64 stepBudgetCm, out Fix64Vec2 next)
        {
            if (projectile.HasDirection == 0)
            {
                next = current;
                return false;
            }

            next = current + projectile.Direction * stepBudgetCm;
            return true;
        }

        private bool TryMoveTrackTarget(in ProjectileState projectile, in Fix64Vec2 current, Fix64 stepBudgetCm, out Fix64Vec2 next, out Fix64 actualStepCm)
        {
            actualStepCm = stepBudgetCm;
            if (World.IsAlive(projectile.Target) && World.Has<WorldPositionCm>(projectile.Target))
            {
                var targetPosition = World.Get<WorldPositionCm>(projectile.Target).Value;
                var delta = targetPosition - current;
                Fix64 distance = delta.Length();

                if (distance <= stepBudgetCm || distance <= Fix64.OneValue)
                {
                    next = targetPosition;
                    actualStepCm = distance;
                    return true;
                }

                next = current + delta.Normalized() * stepBudgetCm;
                return true;
            }

            next = current;
            actualStepCm = Fix64.Zero;
            return false;
        }

        private bool TryMoveLegacy(in ProjectileState projectile, in Fix64Vec2 current, Fix64 stepBudgetCm, out Fix64Vec2 next, out Fix64 actualStepCm)
        {
            actualStepCm = stepBudgetCm;

            if (World.IsAlive(projectile.Target) && World.Has<WorldPositionCm>(projectile.Target))
            {
                var targetPosition = World.Get<WorldPositionCm>(projectile.Target).Value;
                var delta = targetPosition - current;
                Fix64 distance = delta.Length();
                if (distance <= stepBudgetCm || distance <= Fix64.OneValue)
                {
                    next = targetPosition;
                    actualStepCm = distance;
                    return true;
                }

                next = current + delta.Normalized() * stepBudgetCm;
                return false;
            }

            if (projectile.HasTargetPoint != 0)
            {
                var delta = projectile.TargetPointCm - current;
                Fix64 distance = delta.Length();
                if (distance <= stepBudgetCm || distance <= Fix64.OneValue)
                {
                    next = projectile.TargetPointCm;
                    actualStepCm = distance;
                    return true;
                }

                next = current + delta.Normalized() * stepBudgetCm;
                return false;
            }

            if (projectile.HasDirection != 0)
            {
                next = current + projectile.Direction * stepBudgetCm;
                return false;
            }

            next = current + new Fix64Vec2(stepBudgetCm, Fix64.Zero);
            return false;
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

        public override void Dispose()
        {
            _commandBuffer.Dispose();
            base.Dispose();
        }
    }
}
