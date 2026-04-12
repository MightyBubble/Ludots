using System;
using System.Collections.Generic;
using Arch.Buffer;
using Arch.Core;
using Arch.System;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Navigation2D.Components;
using Ludots.Core.Physics2D.Components;

namespace Ludots.Core.Navigation2D.Systems
{
    public sealed class NavGroupLifecycleSystem : BaseSystem<World, float>
    {
        private static readonly QueryDescription GroupMemberQuery = new QueryDescription()
            .WithAll<NavGroupMember, NavCrowdAgent2D, NavGoal2D, Position2D>();

        private static readonly QueryDescription GroupQuery = new QueryDescription()
            .WithAll<NavGroupTag, NavGroupIdentity, NavGroupRuntimeState>();

        private readonly Dictionary<int, GroupTickState> _groupStates = new();
        private readonly CommandBuffer _commandBuffer = new();

        public NavGroupLifecycleSystem(World world) : base(world)
        {
        }

        public override void Update(in float dt)
        {
            _groupStates.Clear();

            foreach (ref var chunk in World.Query(in GroupMemberQuery))
            {
                ref Entity entityFirst = ref chunk.Entity(0);
                Span<NavGroupMember> members = chunk.GetSpan<NavGroupMember>();
                Span<NavCrowdAgent2D> crowdAgents = chunk.GetSpan<NavCrowdAgent2D>();
                Span<NavGoal2D> goals = chunk.GetSpan<NavGoal2D>();
                Span<Position2D> positions = chunk.GetSpan<Position2D>();
                bool hasProgress = chunk.Has<NavAgentProgressState>();
                Span<NavAgentProgressState> progressStates = hasProgress ? chunk.GetSpan<NavAgentProgressState>() : default;
                bool hasDesiredVelocity = chunk.Has<NavDesiredVelocity2D>();
                Span<NavDesiredVelocity2D> desiredVelocities = hasDesiredVelocity ? chunk.GetSpan<NavDesiredVelocity2D>() : default;

                foreach (int index in chunk)
                {
                    Entity entity = System.Runtime.CompilerServices.Unsafe.Add(ref entityFirst, index);
                    int groupId = members[index].GroupId;
                    if (groupId <= 0)
                    {
                        continue;
                    }

                    GroupTickState groupState = GetOrCreateGroupState(groupId);
                    groupState.MemberCount++;

                    NavGoal2D goal = goals[index];
                    if (goal.Kind != NavGoalKind2D.Point)
                    {
                        groupState.ArrivedMemberCount++;
                        _groupStates[groupId] = groupState;
                        continue;
                    }

                    NavAgentProgressState progress = hasProgress ? progressStates[index] : default;
                    if (progress.LastGoalTargetCm != goal.TargetCm || progress.LastGoalRadiusCm != goal.RadiusCm)
                    {
                        progress = default;
                        progress.LastGoalTargetCm = goal.TargetCm;
                        progress.LastGoalRadiusCm = goal.RadiusCm;
                    }

                    Fix64Vec2 delta = goal.TargetCm - positions[index].Value;
                    Fix64 distanceSq = delta.LengthSquared();
                    Fix64 arrivalRadius = goal.RadiusCm + Fix64.FromInt(24);
                    Fix64 arrivalRadiusSq = arrivalRadius * arrivalRadius;
                    if (distanceSq <= arrivalRadiusSq)
                    {
                        progress.StallTicks = 0;
                        progress.TotalStallTicks = 0;
                        progress.LastDistanceCm = Fix64Math.Sqrt(distanceSq);
                        groupState.ArrivedMemberCount++;
                        WriteProgressState(entity, hasProgress, progressStates, index, progress);
                        _groupStates[groupId] = groupState;
                        continue;
                    }

                    Fix64 distanceCm = Fix64Math.Sqrt(distanceSq);
                    bool madeProgress = progress.LastDistanceCm == Fix64.Zero || distanceCm + Fix64.FromInt(20) < progress.LastDistanceCm;
                    bool currentlyMoving = hasDesiredVelocity && desiredVelocities[index].ValueCmPerSec.LengthSquared() > Fix64.FromInt(25) * Fix64.FromInt(25);
                    if (madeProgress || currentlyMoving)
                    {
                        progress.StallTicks = 0;
                    }
                    else
                    {
                        progress.StallTicks++;
                        progress.TotalStallTicks++;
                    }

                    progress.LastDistanceCm = distanceCm;

                    NavCrowdAgent2D crowdAgent = crowdAgents[index];
                    bool timedOut = crowdAgent.TimeoutTicks > 0 && progress.StallTicks >= crowdAgent.TimeoutTicks;
                    bool exhaustedRetries = progress.RetryCount >= crowdAgent.RetryLimit;
                    bool abandonByTime = crowdAgent.AbandonTicks > 0 && progress.TotalStallTicks >= crowdAgent.AbandonTicks;
                    if (timedOut)
                    {
                        groupState.TimeoutEvents++;
                        progress.StallTicks = 0;
                        progress.RetryCount++;

                        Fix64 relaxedRadius = goal.RadiusCm + crowdAgent.GeometryRadiusCm + crowdAgent.GeometryRadiusCm;
                        bool closeEnoughToStop = distanceSq <= relaxedRadius * relaxedRadius;
                        if (closeEnoughToStop || exhaustedRetries || abandonByTime)
                        {
                            groupState.AbandonEvents++;
                            progress.IsAbandoned = 1;
                            goals[index] = new NavGoal2D { Kind = NavGoalKind2D.None };
                            if (hasDesiredVelocity)
                            {
                                desiredVelocities[index] = new NavDesiredVelocity2D { ValueCmPerSec = Fix64Vec2.Zero };
                            }

                            groupState.ArrivedMemberCount++;
                        }
                        else
                        {
                            groupState.RetryEvents++;
                            Fix64 expandedRadius = crowdAgent.GeometryRadiusCm + crowdAgent.GeometryRadiusCm + Fix64.FromInt(40);
                            if (expandedRadius > goal.RadiusCm)
                            {
                                goal.RadiusCm = expandedRadius;
                            }

                            goals[index] = goal;
                        }
                    }

                    WriteProgressState(entity, hasProgress, progressStates, index, progress);
                    _groupStates[groupId] = groupState;
                }
            }

            foreach (ref var chunk in World.Query(in GroupQuery))
            {
                Span<NavGroupIdentity> identities = chunk.GetSpan<NavGroupIdentity>();
                Span<NavGroupRuntimeState> runtimeStates = chunk.GetSpan<NavGroupRuntimeState>();
                foreach (int index in chunk)
                {
                    int groupId = identities[index].GroupId;
                    if (!_groupStates.TryGetValue(groupId, out GroupTickState groupState))
                    {
                        runtimeStates[index].ArrivedMemberCount = 0;
                        runtimeStates[index].IsArrived = 0;
                        continue;
                    }

                    ref NavGroupRuntimeState runtimeState = ref runtimeStates[index];
                    runtimeState.ArrivedMemberCount = groupState.ArrivedMemberCount;
                    runtimeState.IsArrived = (byte)(groupState.MemberCount > 0 && groupState.ArrivedMemberCount >= groupState.MemberCount ? 1 : 0);
                    runtimeState.RetryCount += groupState.RetryEvents;
                    runtimeState.TimeoutCount += groupState.TimeoutEvents;
                    runtimeState.AbandonCount += groupState.AbandonEvents;
                }
            }

            if (_commandBuffer.Size > 0)
            {
                _commandBuffer.Playback(World);
            }
        }

        private void WriteProgressState(Entity entity, bool hasProgress, Span<NavAgentProgressState> progressStates, int index, NavAgentProgressState progress)
        {
            if (hasProgress)
            {
                progressStates[index] = progress;
            }
            else if (!World.Has<NavAgentProgressState>(entity))
            {
                _commandBuffer.Add(entity, progress);
            }
        }

        private GroupTickState GetOrCreateGroupState(int groupId)
        {
            if (_groupStates.TryGetValue(groupId, out GroupTickState state))
            {
                return state;
            }

            return default;
        }

        private struct GroupTickState
        {
            public int MemberCount;
            public int ArrivedMemberCount;
            public int RetryEvents;
            public int TimeoutEvents;
            public int AbandonEvents;
        }
    }
}
