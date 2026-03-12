using System;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Mathematics;
using Ludots.Core.Spatial;
using GasGraphExecutor = Ludots.Core.NodeLibraries.GASGraph.GraphExecutor;

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

    public sealed class ContextScoredOrderResolver
    {
        private readonly World _world;
        private readonly ContextGroupRegistry _contextGroups;
        private readonly GraphProgramRegistry _graphPrograms;
        private readonly Ludots.Core.NodeLibraries.GASGraph.IGraphRuntimeApi _graphApi;
        private readonly ISpatialQueryService _spatialQueries;
        private readonly Entity[] _queryBuffer = new Entity[256];

        public ContextScoredOrderResolver(
            World world,
            ContextGroupRegistry contextGroups,
            GraphProgramRegistry graphPrograms,
            ISpatialQueryService spatialQueries,
            Ludots.Core.NodeLibraries.GASGraph.IGraphRuntimeApi graphApi)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _contextGroups = contextGroups ?? throw new ArgumentNullException(nameof(contextGroups));
            _graphPrograms = graphPrograms ?? throw new ArgumentNullException(nameof(graphPrograms));
            _spatialQueries = spatialQueries ?? throw new ArgumentNullException(nameof(spatialQueries));
            _graphApi = graphApi ?? throw new ArgumentNullException(nameof(graphApi));
        }

        public bool TryResolve(Entity actor, InputOrderMapping mapping, Entity hoveredEntity, out ContextScoredOrderResolution resolution)
        {
            resolution = default;

            if (!_world.IsAlive(actor) || !_world.Has<AbilityStateBuffer>(actor))
            {
                return false;
            }

            if (mapping.ArgsTemplate.I0 is null)
            {
                return false;
            }

            int rootSlotIndex = mapping.ArgsTemplate.I0.Value;
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
                candidateCount = _spatialQueries.QueryRadius(actorWorldCm, group.SearchRadiusCm, _queryBuffer).Count;
            }

            float bestScore = float.MinValue;
            int bestSlotIndex = -1;
            Entity bestTarget = default;

            for (int i = 0; i < group.Candidates.Count; i++)
            {
                var candidate = group.Candidates[i];
                if (!TryFindSlotIndexForAbility(actor, candidate.AbilityId, out int candidateSlotIndex))
                {
                    continue;
                }

                if (!candidate.RequiresTarget)
                {
                    if (TryScoreCandidate(actor, default, hoveredEntity, actorWorldCm, candidate, out float score) && score > bestScore)
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

                    if (TryScoreCandidate(actor, target, hoveredEntity, actorWorldCm, candidate, out float score) && score > bestScore)
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
            ref var abilities = ref _world.Get<AbilityStateBuffer>(actor);
            if ((uint)rootSlotIndex >= (uint)abilities.Count)
            {
                return false;
            }

            bool hasForm = _world.Has<AbilityFormSlotBuffer>(actor);
            AbilityFormSlotBuffer formSlots = hasForm ? _world.Get<AbilityFormSlotBuffer>(actor) : default;
            bool hasGranted = _world.Has<GrantedSlotBuffer>(actor);
            GrantedSlotBuffer granted = hasGranted ? _world.Get<GrantedSlotBuffer>(actor) : default;
            var slot = AbilitySlotResolver.Resolve(in abilities, in formSlots, hasForm, in granted, hasGranted, rootSlotIndex);
            return slot.AbilityId > 0 && _contextGroups.TryGetByRootAbility(slot.AbilityId, out group);
        }

        private bool TryFindSlotIndexForAbility(Entity actor, int abilityId, out int slotIndex)
        {
            slotIndex = -1;
            ref var abilities = ref _world.Get<AbilityStateBuffer>(actor);
            bool hasForm = _world.Has<AbilityFormSlotBuffer>(actor);
            AbilityFormSlotBuffer formSlots = hasForm ? _world.Get<AbilityFormSlotBuffer>(actor) : default;
            bool hasGranted = _world.Has<GrantedSlotBuffer>(actor);
            GrantedSlotBuffer granted = hasGranted ? _world.Get<GrantedSlotBuffer>(actor) : default;

            for (int i = 0; i < abilities.Count; i++)
            {
                var slot = AbilitySlotResolver.Resolve(in abilities, in formSlots, hasForm, in granted, hasGranted, i);
                if (slot.AbilityId == abilityId)
                {
                    slotIndex = i;
                    return true;
                }
            }

            return false;
        }

        private bool TryScoreCandidate(
            Entity actor,
            Entity target,
            Entity hoveredEntity,
            WorldCmInt2 actorWorldCm,
            in ContextGroupCandidate candidate,
            out float totalScore)
        {
            totalScore = candidate.BasePriority;
            WorldCmInt2 targetWorldCm = default;

            if (candidate.RequiresTarget)
            {
                if (!_world.TryGet(target, out WorldPositionCm targetPosition))
                {
                    return false;
                }

                targetWorldCm = targetPosition.Value.ToWorldCmInt2();

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
                if (!_graphPrograms.TryGetProgram(candidate.PreconditionGraphId, out var preconditionProgram))
                {
                    throw new InvalidOperationException($"Missing precondition graph id {candidate.PreconditionGraphId}.");
                }

                if (!GasGraphExecutor.ExecuteValidation(_world, actor, target, default, preconditionProgram, _graphApi))
                {
                    return false;
                }
            }

            if (candidate.ScoreGraphId > 0)
            {
                if (!_graphPrograms.TryGetProgram(candidate.ScoreGraphId, out var scoreProgram))
                {
                    throw new InvalidOperationException($"Missing score graph id {candidate.ScoreGraphId}.");
                }

                totalScore += GasGraphExecutor.ExecuteScore(_world, actor, target, default, scoreProgram, _graphApi);
            }

            return true;
        }

        private static float ComputeDistanceCm(WorldCmInt2 a, WorldCmInt2 b)
        {
            float dx = b.X - a.X;
            float dy = b.Y - a.Y;
            return MathF.Sqrt(dx * dx + dy * dy);
        }

        private static float ComputeAngleToTargetDeg(WorldCmInt2 actorWorldCm, WorldCmInt2 targetWorldCm, float facingAngleRad)
        {
            float dx = targetWorldCm.X - actorWorldCm.X;
            float dy = targetWorldCm.Y - actorWorldCm.Y;
            float targetAngle = MathF.Atan2(dy, dx);
            float delta = NormalizeAngleRad(targetAngle - facingAngleRad);
            return MathF.Abs(delta) * (180f / MathF.PI);
        }

        private static float NormalizeAngleRad(float angle)
        {
            while (angle > MathF.PI)
            {
                angle -= MathF.Tau;
            }

            while (angle < -MathF.PI)
            {
                angle += MathF.Tau;
            }

            return angle;
        }
    }
}
