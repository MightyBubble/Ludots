using System.Numerics;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Navigation2D.Components;
using Ludots.Core.Physics2D.Components;
using MassFlowNavPlaygroundMod.Components;
using MassFlowNavPlaygroundMod.Runtime;

namespace MassFlowNavPlaygroundMod.Systems
{
    internal sealed class MassFlowNavPlaygroundFormationRuntimeSystem : ISystem<float>
    {
        private readonly GameEngine _engine;
        private readonly World _world;
        private int[] _groupIdScratch = Array.Empty<int>();

        public MassFlowNavPlaygroundFormationRuntimeSystem(GameEngine engine)
        {
            _engine = engine;
            _world = engine.World;
        }

        public void Initialize() { }
        public void BeforeUpdate(in float t) { }
        public void AfterUpdate(in float t) { }
        public void Dispose() { }

        public void Update(in float t)
        {
            if (_engine.GetService(MassFlowNavPlaygroundServiceKeys.State) is not MassFlowNavPlaygroundState state ||
                !state.IsActive ||
                state.Groups.Count == 0)
            {
                return;
            }

            state.FormationFrameIndex++;
            if ((state.FormationFrameIndex & 1) == 0)
            {
                return;
            }

            int groupCount = state.Groups.Count;
            if (groupCount <= 0)
            {
                return;
            }

            EnsureGroupCapacity(groupCount);
            int index = 0;
            foreach (int groupId in state.Groups.Keys)
            {
                _groupIdScratch[index++] = groupId;
            }

            for (int i = 0; i < index; i++)
            {
                int groupId = _groupIdScratch[i];
                if (!state.CompactGroup(_world, groupId) || !state.TryGetGroup(groupId, out MassFlowFormationGroup group))
                {
                    continue;
                }

                Vector2 centroid = Vector2.Zero;
                int aliveCount = 0;
                for (int memberIndex = 0; memberIndex < group.Members.Count; memberIndex++)
                {
                    Entity member = group.Members[memberIndex];
                    if (!TryGetPositionCm(member, out Vector2 positionCm))
                    {
                        continue;
                    }

                    centroid += positionCm;
                    aliveCount++;
                }

                if (aliveCount > 0)
                {
                    centroid /= aliveCount;
                }

                group.CentroidCm = centroid;
                float maxDistanceSq = 0f;

                for (int memberIndex = 0; memberIndex < group.Members.Count; memberIndex++)
                {
                    Entity member = group.Members[memberIndex];
                    if (!_world.IsAlive(member))
                    {
                        continue;
                    }

                    Vector2 slotTarget = group.DestinationCm + group.OffsetsCm[memberIndex];
                    Vector2 delta = slotTarget - centroid;
                    float distanceSq = delta.LengthSquared();
                    if (distanceSq > maxDistanceSq)
                    {
                        maxDistanceSq = distanceSq;
                    }

                    EnsureManualTag(state, member);
                    EnsureSmartStopSuppressed(member);

                    _world.Set(member, new MassFlowNavFormationMember
                    {
                        GroupId = group.GroupId,
                        SlotIndex = memberIndex
                    });

                    UpsertPointGoal(member, slotTarget);
                }

                float arrivalRadiusCm = group.Members.Count <= 1 ? 120f : 180f;
                group.Arrived = maxDistanceSq <= arrivalRadiusCm * arrivalRadiusCm;
            }
        }

        private void EnsureGroupCapacity(int required)
        {
            if (required <= _groupIdScratch.Length)
            {
                return;
            }

            int nextSize = _groupIdScratch.Length == 0 ? 16 : _groupIdScratch.Length;
            while (nextSize < required)
            {
                nextSize *= 2;
            }

            Array.Resize(ref _groupIdScratch, nextSize);
        }

        private void EnsureManualTag(MassFlowNavPlaygroundState state, Entity member)
        {
            if (_world.Has<MassFlowNavManualGoalTag>(member))
            {
                return;
            }

            _world.Add(member, default(MassFlowNavManualGoalTag));
            state.IncrementManualCount();
        }

        private void EnsureSmartStopSuppressed(Entity member)
        {
            if (!_world.IsAlive(member) || !_world.Has<NavAgent2D>(member))
            {
                return;
            }

            ref var navAgent = ref _world.Get<NavAgent2D>(member);
            navAgent.SmartStopSuppressed = 1;
        }

        private bool TryGetPositionCm(Entity entity, out Vector2 positionCm)
        {
            positionCm = Vector2.Zero;
            if (!_world.IsAlive(entity))
            {
                return false;
            }

            if (_world.TryGet(entity, out WorldPositionCm worldPosition))
            {
                positionCm = worldPosition.Value.ToVector2();
                return true;
            }

            if (_world.TryGet(entity, out Position2D position))
            {
                positionCm = position.Value.ToVector2();
                return true;
            }

            return false;
        }

        private void UpsertPointGoal(Entity entity, Vector2 targetCm)
        {
            var goal = new NavGoal2D
            {
                Kind = NavGoalKind2D.Point,
                TargetCm = Fix64Vec2.FromFloat(targetCm.X, targetCm.Y),
                RadiusCm = Fix64.FromInt(60)
            };

            if (_world.Has<NavGoal2D>(entity))
            {
                _world.Set(entity, goal);
            }
            else
            {
                _world.Add(entity, goal);
            }
        }
    }
}
