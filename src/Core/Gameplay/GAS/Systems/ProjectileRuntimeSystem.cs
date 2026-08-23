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
using Ludots.Core.Mathematics;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Spatial;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Gameplay.GAS.Systems
{
    public sealed class ProjectileRuntimeSystem : BaseSystem<World, float>
    {
        public const string CollisionCandidateCapacityExceededError =
            "GAS.PROJECTILE.ERR.CollisionCandidateCapacityExceeded";
        public const string RuntimeEntityCapacityExceededError =
            "GAS.PROJECTILE.ERR.RuntimeEntityCapacityExceeded";
        public const string InvalidMaxHitCountError =
            "GAS.PROJECTILE.ERR.InvalidMaxHitCount";

        private static readonly QueryDescription Query = new QueryDescription().WithAll<ProjectileState, WorldPositionCm>();
        private readonly EffectRequestQueue _effectRequests;
        private readonly ISpatialQueryService _spatialQueries;
        private readonly Entity[] _collisionCandidates;
        private readonly Entity[] _toDestroy;
        private readonly HashSet<Entity> _toDestroySet;
        private int _toDestroyCount;
        private readonly CommandBuffer _commandBuffer = new();

        public int CollisionCandidateCapacity => _collisionCandidates.Length;
        public int RuntimeEntityCapacity => _toDestroy.Length;

        public ProjectileRuntimeSystem(
            World world,
            EffectRequestQueue effectRequests,
            ISpatialQueryService spatialQueries,
            int collisionCandidateCapacity,
            int runtimeEntityCapacity) : base(world)
        {
            if (collisionCandidateCapacity <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(collisionCandidateCapacity),
                    collisionCandidateCapacity,
                    "capacity must be positive.");
            }
            if (runtimeEntityCapacity <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(runtimeEntityCapacity),
                    runtimeEntityCapacity,
                    "capacity must be positive.");
            }

            _effectRequests = effectRequests;
            _spatialQueries = spatialQueries;
            _collisionCandidates = new Entity[collisionCandidateCapacity];
            _toDestroy = new Entity[runtimeEntityCapacity];
            _toDestroySet = new HashSet<Entity>(runtimeEntityCapacity);
        }

        public override void Update(in float dt)
        {
            if (_effectRequests == null)
            {
                return;
            }

            _toDestroyCount = 0;
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

            for (int i = 0; i < _toDestroyCount; i++)
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
            if (projectile.HitEffectTemplateId > 0 &&
                projectile.CollisionHalfWidthCm > 0 &&
                (projectile.MaxHitCount <= 0 || projectile.MaxHitCount > ProjectileState.HitHistoryCapacity))
            {
                throw new InvalidOperationException(
                    $"{InvalidMaxHitCountError}: value={projectile.MaxHitCount}, supported=1..{ProjectileState.HitHistoryCapacity}.");
            }

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
                        next = MoveDirection(in projectile, current, stepBudgetCm);
                        break;

                    case ProjectileTravelMode.TrackTarget:
                        completed = !TryMoveTrackTarget(in projectile, current, stepBudgetCm, out next, out actualStepCm);
                        break;

                    default:
                        throw new InvalidOperationException(
                            $"Projectile has unsupported travel mode value {(byte)projectile.TravelMode}.");
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
                projectile.CollisionHalfWidthCm <= 0)
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
            Span<Entity> rawHits = _collisionCandidates;
            SpatialQueryResult queryResult = _spatialQueries.QueryLine(
                current.ToWorldCmInt2(),
                directionDeg,
                lengthCm,
                projectile.CollisionHalfWidthCm,
                rawHits);
            if (queryResult.Dropped > 0)
            {
                throw new InvalidOperationException(
                    $"{CollisionCandidateCapacityExceededError}: capacity={rawHits.Length}, dropped={queryResult.Dropped}.");
            }

            int hitCount = queryResult.Count;
            if (hitCount <= 0)
            {
                return false;
            }

            Span<Entity> orderedHits = stackalloc Entity[ProjectileState.HitHistoryCapacity];
            Span<int> projections = stackalloc int[ProjectileState.HitHistoryCapacity];
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

                int insertAt = 0;
                while (insertAt < orderedCount &&
                       !ComesBefore(projection, candidate, projections[insertAt], orderedHits[insertAt]))
                {
                    insertAt++;
                }

                if (insertAt >= orderedHits.Length)
                {
                    continue;
                }

                int lastIndex = Math.Min(orderedCount, orderedHits.Length - 1);
                for (int move = lastIndex; move > insertAt; move--)
                {
                    projections[move] = projections[move - 1];
                    orderedHits[move] = orderedHits[move - 1];
                }

                projections[insertAt] = projection;
                orderedHits[insertAt] = candidate;
                if (orderedCount < orderedHits.Length)
                {
                    orderedCount++;
                }
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
            if (_toDestroySet.Contains(entity))
            {
                return;
            }

            if (_toDestroyCount >= _toDestroy.Length)
            {
                throw new InvalidOperationException(
                    $"{RuntimeEntityCapacityExceededError}: capacity={_toDestroy.Length}.");
            }

            _toDestroySet.Add(entity);
            _toDestroy[_toDestroyCount++] = entity;
        }

        private static bool ComesBefore(int projection, Entity candidate, int otherProjection, Entity other)
        {
            if (projection != otherProjection)
            {
                return projection < otherProjection;
            }
            if (candidate.WorldId != other.WorldId)
            {
                return candidate.WorldId < other.WorldId;
            }
            if (candidate.Id != other.Id)
            {
                return candidate.Id < other.Id;
            }
            return candidate.Version < other.Version;
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

        private static Fix64Vec2 MoveDirection(in ProjectileState projectile, in Fix64Vec2 current, Fix64 stepBudgetCm)
        {
            if (projectile.HasDirection == 0)
            {
                throw new InvalidOperationException("Projectile direction mode requires HasDirection=1.");
            }

            return current + projectile.Direction * stepBudgetCm;
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

        private static int ComputeDirectionDeg(in Fix64Vec2 delta)
        {
            return WorldPlane2D.FacingDegreesPositiveFromDirection(in delta);
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
