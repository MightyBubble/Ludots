using System;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.Items;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Mathematics;
using Ludots.Core.Spatial;

namespace Ludots.Core.Input.Orders
{
    public readonly struct ContextScoredOrderResolution
    {
        public ContextScoredOrderResolution(int slotIndex, Entity target, Vector3 targetWorldCm, bool hasTargetWorldCm)
        {
            SlotIndex = slotIndex;
            Target = target;
            TargetWorldCm = targetWorldCm;
            HasTargetWorldCm = hasTargetWorldCm;
        }

        public int SlotIndex { get; }
        public Entity Target { get; }
        public Vector3 TargetWorldCm { get; }
        public bool HasTargetWorldCm { get; }
    }

    /// <summary>
    /// Knowledge gate for spatial candidates (RFC-0065 INT-4): true when <paramref name="viewer"/> is
    /// allowed to command-target <paramref name="candidate"/> (<c>CanTargetCommand</c> semantics). The
    /// resolver invokes it with the acting entity as viewer.
    /// </summary>
    public delegate bool ContextScoredCandidateGate(Entity viewer, Entity candidate);

    public sealed class ContextScoredOrderResolver
    {
        private readonly World _world;
        private readonly ContextGroupRegistry _contextGroups;
        private readonly IReadOnlyGraphScorer _graphScorer;
        private readonly ISpatialQueryService _spatialQueries;
        private readonly ContextScoredCandidateGate _candidateGate;
        private readonly int _graphInstructionBudget;
        private readonly Entity[] _queryBuffer;

        public ContextScoredOrderResolver(
            World world,
            ContextGroupRegistry contextGroups,
            IReadOnlyGraphScorer graphScorer,
            ISpatialQueryService spatialQueries,
            ContextScoredCandidateGate candidateGate,
            int graphInstructionBudget,
            int candidateCapacity)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _contextGroups = contextGroups ?? throw new ArgumentNullException(nameof(contextGroups));
            _graphScorer = graphScorer ?? throw new ArgumentNullException(nameof(graphScorer));
            _spatialQueries = spatialQueries ?? throw new ArgumentNullException(nameof(spatialQueries));
            _candidateGate = candidateGate ?? throw new ArgumentNullException(nameof(candidateGate));
            _graphInstructionBudget = ValidateGraphInstructionBudget(graphInstructionBudget);
            _queryBuffer = new Entity[ValidateCandidateCapacity(candidateCapacity)];
        }

        public bool TryResolve(Entity actor, InputOrderMapping mapping, Entity hoveredEntity, out ContextScoredOrderResolution resolution)
            => TryResolve(actor, mapping, hoveredEntity, out resolution, out _);

        public bool TryResolve(
            Entity actor,
            InputOrderMapping mapping,
            Entity hoveredEntity,
            out ContextScoredOrderResolution resolution,
            out GraphScoreFailureReason failureReason)
        {
            resolution = default;
            failureReason = GraphScoreFailureReason.None;

            if (!_world.IsAlive(actor) || !_world.Has<AbilityStateBuffer>(actor))
            {
                return false;
            }

            if (!mapping.TryResolveAbilitySlot(out int rootSlotIndex))
            {
                return false;
            }

            if (!TryResolveContextGroup(actor, rootSlotIndex, out var group))
            {
                return false;
            }

            if (!_world.TryGet(actor, out WorldPositionCm actorPosition))
            {
                return false;
            }

            var actorWorldCm = actorPosition.Value.ToWorldCmInt2();
            int candidateCount = 0;
            if (group.SearchRadiusCm > 0)
            {
                SpatialQueryResult queryResult = _spatialQueries.QueryRadius(actorWorldCm, group.SearchRadiusCm, _queryBuffer);
                if (queryResult.Dropped > 0)
                {
                    failureReason = GraphScoreFailureReason.BudgetExhausted;
                    return false;
                }

                candidateCount = queryResult.Count;
                if (candidateCount > 0)
                {
                    // INT-4: compact in place so only viewer-knowable candidates reach scoring.
                    int kept = 0;
                    for (int i = 0; i < candidateCount; i++)
                    {
                        if (_candidateGate(actor, _queryBuffer[i]))
                        {
                            _queryBuffer[kept++] = _queryBuffer[i];
                        }
                    }

                    candidateCount = kept;
                }
            }

            float bestScore = float.MinValue;
            int bestSlotIndex = -1;
            Entity bestTarget = default;
            GraphInstructionBudget graphBudget = GraphInstructionBudget.Create(_graphInstructionBudget);

            for (int i = 0; i < group.Candidates.Count; i++)
            {
                var candidate = group.Candidates[i];
                if (!TryFindSlotIndexForAbility(actor, candidate.AbilityId, out int candidateSlotIndex))
                {
                    continue;
                }

                if (!candidate.RequiresTarget)
                {
                    if (!TryScoreCandidate(
                            actor,
                            default,
                            hoveredEntity,
                            actorWorldCm,
                            candidate,
                            ref graphBudget,
                            out float score,
                            out failureReason))
                    {
                        if (failureReason != GraphScoreFailureReason.None)
                        {
                            resolution = default;
                            return false;
                        }

                        continue;
                    }

                    if (IsBetterCandidate(score, default, candidateSlotIndex, bestScore, bestTarget, bestSlotIndex))
                    {
                        bestScore = score;
                        bestSlotIndex = candidateSlotIndex;
                        bestTarget = default;
                    }
                    continue;
                }

                for (int targetIndex = 0; targetIndex < candidateCount; targetIndex++)
                {
                    Entity target = _queryBuffer[targetIndex];
                    if (!_world.IsAlive(target) || target.Equals(actor))
                    {
                        continue;
                    }

                    if (!TryScoreCandidate(
                            actor,
                            target,
                            hoveredEntity,
                            actorWorldCm,
                            candidate,
                            ref graphBudget,
                            out float score,
                            out failureReason))
                    {
                        if (failureReason != GraphScoreFailureReason.None)
                        {
                            resolution = default;
                            return false;
                        }

                        continue;
                    }

                    if (IsBetterCandidate(score, target, candidateSlotIndex, bestScore, bestTarget, bestSlotIndex))
                    {
                        bestScore = score;
                        bestSlotIndex = candidateSlotIndex;
                        bestTarget = target;
                    }
                }
            }

            if (bestSlotIndex < 0)
            {
                return false;
            }

            resolution = new ContextScoredOrderResolution(bestSlotIndex, bestTarget, default, hasTargetWorldCm: false);
            return true;
        }

        private bool TryResolveContextGroup(Entity actor, int rootSlotIndex, out ContextGroupDefinition group)
        {
            group = default;
            return AbilitySlotResolver.TryResolve(_world, actor, rootSlotIndex, out AbilitySlotState slot) &&
                   slot.AbilityId > 0 &&
                   _contextGroups.TryGetByRootAbility(slot.AbilityId, out group);
        }

        private bool TryFindSlotIndexForAbility(Entity actor, int abilityId, out int slotIndex)
        {
            slotIndex = -1;
            return AbilitySlotResolver.TryFindAbility(_world, actor, abilityId, out slotIndex);
        }

        private bool TryScoreCandidate(
            Entity actor,
            Entity target,
            Entity hoveredEntity,
            WorldCmInt2 actorWorldCm,
            in ContextGroupCandidate candidate,
            ref GraphInstructionBudget graphBudget,
            out float totalScore,
            out GraphScoreFailureReason failureReason)
        {
            totalScore = candidate.BasePriority;
            failureReason = GraphScoreFailureReason.None;
            WorldCmInt2 targetWorldCm = default;
            IntVector2 graphTargetPosCm = default;

            if (candidate.RequiresTarget)
            {
                if (!_world.TryGet(target, out WorldPositionCm targetPosition))
                {
                    return false;
                }

                targetWorldCm = targetPosition.Value.ToWorldCmInt2();
                graphTargetPosCm = new IntVector2(targetWorldCm.X, targetWorldCm.Y);

                if (candidate.MaxDistanceCm > 0)
                {
                    float distanceCm = ComputeDistanceCm(actorWorldCm, targetWorldCm);
                    if (distanceCm > candidate.MaxDistanceCm)
                    {
                        return false;
                    }

                    if (candidate.DistanceWeight != 0f)
                    {
                        float normalized = 1f - Math.Clamp(distanceCm / candidate.MaxDistanceCm, 0f, 1f);
                        totalScore += normalized * candidate.DistanceWeight;
                    }
                }

                if (candidate.MaxAngleDeg > 0 && candidate.AngleWeight != 0f && _world.TryGet(actor, out FacingDirection facing))
                {
                    float angleDeg = ComputeAngleToTargetDeg(actorWorldCm, targetWorldCm, facing.AngleRad);
                    if (angleDeg > candidate.MaxAngleDeg)
                    {
                        return false;
                    }

                    float normalized = 1f - Math.Clamp(angleDeg / candidate.MaxAngleDeg, 0f, 1f);
                    totalScore += normalized * candidate.AngleWeight;
                }

                if (!hoveredEntity.Equals(default) && hoveredEntity.Equals(target))
                {
                    totalScore += candidate.HoveredBiasScore;
                }
            }

            if (candidate.PreconditionGraphId > 0)
            {
                if (!_graphScorer.TryEvaluateValidation(
                        actor,
                        target,
                        graphTargetPosCm,
                        candidate.PreconditionGraphId,
                        ref graphBudget,
                        out bool passed,
                        out failureReason))
                {
                    ThrowIfGraphContractFailure(candidate.PreconditionGraphId, failureReason, GraphKind.Validation);
                    return false;
                }

                if (!passed)
                {
                    return false;
                }
            }

            if (candidate.ScoreGraphId > 0)
            {
                if (!_graphScorer.TryEvaluateScore(
                        actor,
                        target,
                        graphTargetPosCm,
                        candidate.ScoreGraphId,
                        ref graphBudget,
                        out float graphScore,
                        out failureReason))
                {
                    ThrowIfGraphContractFailure(candidate.ScoreGraphId, failureReason, GraphKind.Score);
                    return false;
                }

                totalScore += graphScore;
            }

            return true;
        }

        private static void ThrowIfGraphContractFailure(int graphId, GraphScoreFailureReason failureReason, GraphKind expectedKind)
        {
            if (failureReason == GraphScoreFailureReason.BudgetExhausted)
            {
                return;
            }

            throw new InvalidOperationException(
                $"Context-scored order graph id {graphId} failed graph-score contract for {expectedKind}: {failureReason}.");
        }

        private static float ComputeDistanceCm(WorldCmInt2 a, WorldCmInt2 b)
        {
            float dx = b.X - a.X;
            float dy = b.Y - a.Y;
            return MathF.Sqrt(dx * dx + dy * dy);
        }

        private static bool IsBetterCandidate(
            float score,
            Entity target,
            int slotIndex,
            float bestScore,
            Entity bestTarget,
            int bestSlotIndex)
        {
            if (score > bestScore)
            {
                return true;
            }

            if (score < bestScore)
            {
                return false;
            }

            int entityCompare = CompareEntityId(target, bestTarget);
            if (entityCompare != 0)
            {
                return entityCompare < 0;
            }

            return bestSlotIndex < 0 || slotIndex < bestSlotIndex;
        }

        private static int CompareEntityId(Entity left, Entity right)
        {
            int id = left.Id.CompareTo(right.Id);
            if (id != 0)
            {
                return id;
            }

            int worldId = left.WorldId.CompareTo(right.WorldId);
            if (worldId != 0)
            {
                return worldId;
            }

            return left.Version.CompareTo(right.Version);
        }

        private static int ValidateGraphInstructionBudget(int value)
        {
            if (value <= 0 || value == int.MaxValue)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    value,
                    "Context-scored graph instruction budget must be positive and finite.");
            }

            return value;
        }

        private static int ValidateCandidateCapacity(int value)
        {
            if (value <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    value,
                    "Context-scored candidate capacity must be positive.");
            }

            return value;
        }

        private static float ComputeAngleToTargetDeg(WorldCmInt2 actorWorldCm, WorldCmInt2 targetWorldCm, float facingAngleRad)
        {
            float dx = targetWorldCm.X - actorWorldCm.X;
            float dy = targetWorldCm.Y - actorWorldCm.Y;
            float targetAngle = WorldPlane2D.FacingRadFromDirection(dx, dy);
            return WorldPlane2D.RadToDegValue(WorldPlane2D.AngleDistanceRad(targetAngle, facingAngleRad));
        }
    }
}
