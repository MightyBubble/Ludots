using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Threading;
using Arch.LowLevel;

namespace Ludots.Core.Navigation2D.Runtime
{
    public readonly struct Navigation2DWorldSyncResult
    {
        public readonly bool SpatialDirty;
        public readonly bool SmartStopDirty;

        public Navigation2DWorldSyncResult(bool spatialDirty, bool smartStopDirty)
        {
            SpatialDirty = spatialDirty;
            SmartStopDirty = smartStopDirty;
        }
    }

    public sealed unsafe class Navigation2DWorld : IDisposable
    {
        public readonly Navigation2DWorldSettings Settings;

        public UnsafeList<int> EntityIds;
        public UnsafeArray<int> EntityToAgentIndex;
        public UnsafeList<Vector2> Positions;
        public UnsafeList<Vector2> Velocities;
        public UnsafeList<float> Radii;
        public UnsafeList<Vector2> GoalPositions;
        public UnsafeList<float> GoalRadii;
        public UnsafeList<float> GoalDistances;
        public UnsafeList<byte> HasPointGoals;
        public UnsafeList<byte> SmartStopFlags;
        public UnsafeList<int> SpatialDirtyAgentIndices;

        private UnsafeList<int> _seenSyncStamps;
        private UnsafeArray<int> _steadySpatialDirtyStamps;
        private int _syncStamp;
        private int _steadySpatialDirtyStamp;
        private bool _spatialDirty;
        private bool _smartStopDirty;
        private int _steadySpatialDirty;
        private int _steadySmartStopDirty;
        private int _steadyFallbackRequired;

        public int Count => Positions.Count;

        public Navigation2DWorld(Navigation2DWorldSettings settings)
        {
            Settings = settings;

            int initialEntityCapacity = Math.Max(8, settings.MaxAgents);
            EntityIds = new UnsafeList<int>(settings.MaxAgents);
            EntityToAgentIndex = new UnsafeArray<int>(initialEntityCapacity);
            UnsafeArray.Fill(ref EntityToAgentIndex, -1);

            Positions = new UnsafeList<Vector2>(settings.MaxAgents);
            Velocities = new UnsafeList<Vector2>(settings.MaxAgents);
            Radii = new UnsafeList<float>(settings.MaxAgents);
            GoalPositions = new UnsafeList<Vector2>(settings.MaxAgents);
            GoalRadii = new UnsafeList<float>(settings.MaxAgents);
            GoalDistances = new UnsafeList<float>(settings.MaxAgents);
            HasPointGoals = new UnsafeList<byte>(settings.MaxAgents);
            SmartStopFlags = new UnsafeList<byte>(settings.MaxAgents);
            SpatialDirtyAgentIndices = new UnsafeList<int>(settings.MaxAgents);
            _seenSyncStamps = new UnsafeList<int>(settings.MaxAgents);
            _steadySpatialDirtyStamps = new UnsafeArray<int>(Math.Max(8, settings.MaxAgents));
            _syncStamp = 0;
            _steadySpatialDirtyStamp = 0;
            _spatialDirty = true;
            _smartStopDirty = true;
        }

        public void BeginSync()
        {
            if (_syncStamp == int.MaxValue)
            {
                if (_seenSyncStamps.Count > 0)
                {
                    _seenSyncStamps.AsSpan().Clear();
                }

                _syncStamp = 0;
            }

            _syncStamp++;
            _spatialDirty = false;
            _smartStopDirty = false;
            SpatialDirtyAgentIndices.Clear();
        }

        public void BeginSteadyStateUpdate()
        {
            if (_steadySpatialDirtyStamp == int.MaxValue)
            {
                UnsafeArray.Fill(ref _steadySpatialDirtyStamps, 0);
                _steadySpatialDirtyStamp = 0;
            }

            _steadySpatialDirtyStamp++;
            _steadySpatialDirty = 0;
            _steadySmartStopDirty = 0;
            _steadyFallbackRequired = 0;
            SpatialDirtyAgentIndices.Clear();
        }

        public void MarkSteadyStateFallbackRequired()
        {
            Interlocked.Exchange(ref _steadyFallbackRequired, 1);
        }

        public bool RequiresFullResync()
        {
            return Volatile.Read(ref _steadyFallbackRequired) != 0;
        }

        public Navigation2DWorldSyncResult EndSteadyStateUpdate()
        {
            bool spatialDirty = Volatile.Read(ref _steadySpatialDirty) != 0;
            if (spatialDirty)
            {
                CollectSteadySpatialDirtyAgentIndices();
            }

            return new Navigation2DWorldSyncResult(
                spatialDirty: spatialDirty,
                smartStopDirty: Volatile.Read(ref _steadySmartStopDirty) != 0);
        }

        public void UpdateExistingAgent(
            int agentIndex,
            in Vector2 position,
            in Vector2 velocity,
            float radius,
            bool hasPointGoal,
            in Vector2 goalPosition,
            float goalRadius,
            float goalDistance)
        {
            byte hasPointGoalByte = hasPointGoal ? (byte)1 : (byte)0;

            if (Positions[agentIndex] != position)
            {
                Positions[agentIndex] = position;
                MarkSteadySpatialDirtyAgent(agentIndex);
                Interlocked.Exchange(ref _steadySpatialDirty, 1);
                Interlocked.Exchange(ref _steadySmartStopDirty, 1);
            }
            else
            {
                Positions[agentIndex] = position;
            }

            if (Velocities[agentIndex] != velocity ||
                GoalPositions[agentIndex] != goalPosition ||
                GoalRadii[agentIndex] != goalRadius ||
                GoalDistances[agentIndex] != goalDistance ||
                HasPointGoals[agentIndex] != hasPointGoalByte)
            {
                Interlocked.Exchange(ref _steadySmartStopDirty, 1);
            }

            Velocities[agentIndex] = velocity;
            Radii[agentIndex] = radius;
            GoalPositions[agentIndex] = goalPosition;
            GoalRadii[agentIndex] = goalRadius;
            GoalDistances[agentIndex] = goalDistance;
            HasPointGoals[agentIndex] = hasPointGoalByte;
        }

        public bool SyncAgent(
            int entityId,
            in Vector2 position,
            in Vector2 velocity,
            float radius,
            bool hasPointGoal,
            in Vector2 goalPosition,
            float goalRadius,
            float goalDistance)
        {
            if (entityId < 0)
            {
                return false;
            }

            byte hasPointGoalByte = hasPointGoal ? (byte)1 : (byte)0;
            if (!TryGetAgentIndex(entityId, out int agentIndex))
            {
                if (Count >= Settings.MaxAgents)
                {
                    return false;
                }

                EnsureEntityIndexCapacity(entityId);

                agentIndex = Count;
                EntityIds.Add(entityId);
                _seenSyncStamps.Add(_syncStamp);
                EntityToAgentIndex[entityId] = agentIndex;

                Positions.Add(position);
                Velocities.Add(velocity);
                Radii.Add(radius);
                GoalPositions.Add(goalPosition);
                GoalRadii.Add(goalRadius);
                GoalDistances.Add(goalDistance);
                HasPointGoals.Add(hasPointGoalByte);
                SmartStopFlags.Add(0);
                SpatialDirtyAgentIndices.Add(agentIndex);

                _spatialDirty = true;
                _smartStopDirty = true;
                return true;
            }

            _seenSyncStamps[agentIndex] = _syncStamp;

            if (Positions[agentIndex] != position)
            {
                Positions[agentIndex] = position;
                SpatialDirtyAgentIndices.Add(agentIndex);
                _spatialDirty = true;
                _smartStopDirty = true;
            }
            else
            {
                Positions[agentIndex] = position;
            }

            if (Velocities[agentIndex] != velocity ||
                GoalPositions[agentIndex] != goalPosition ||
                GoalRadii[agentIndex] != goalRadius ||
                GoalDistances[agentIndex] != goalDistance ||
                HasPointGoals[agentIndex] != hasPointGoalByte)
            {
                _smartStopDirty = true;
            }

            Velocities[agentIndex] = velocity;
            Radii[agentIndex] = radius;
            GoalPositions[agentIndex] = goalPosition;
            GoalRadii[agentIndex] = goalRadius;
            GoalDistances[agentIndex] = goalDistance;
            HasPointGoals[agentIndex] = hasPointGoalByte;
            return true;
        }

        public Navigation2DWorldSyncResult EndSync()
        {
            bool removedAny = RemoveMissingAgents();
            if (removedAny)
            {
                SpatialDirtyAgentIndices.Clear();
            }

            return new Navigation2DWorldSyncResult(
                spatialDirty: _spatialDirty || removedAny,
                smartStopDirty: _smartStopDirty || removedAny);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGetAgentIndex(int entityId, out int agentIndex)
        {
            if ((uint)entityId >= (uint)EntityToAgentIndex.Length)
            {
                agentIndex = -1;
                return false;
            }

            agentIndex = EntityToAgentIndex[entityId];
            return (uint)agentIndex < (uint)Count;
        }

        public void Clear()
        {
            var entityIds = EntityIds.AsSpan();
            for (int i = 0; i < entityIds.Length; i++)
            {
                int entityId = entityIds[i];
                if ((uint)entityId < (uint)EntityToAgentIndex.Length)
                {
                    EntityToAgentIndex[entityId] = -1;
                }
            }

            EntityIds.Clear();
            Positions.Clear();
            Velocities.Clear();
            Radii.Clear();
            GoalPositions.Clear();
            GoalRadii.Clear();
            GoalDistances.Clear();
            HasPointGoals.Clear();
            SmartStopFlags.Clear();
            SpatialDirtyAgentIndices.Clear();
            _seenSyncStamps.Clear();
            UnsafeArray.Fill(ref _steadySpatialDirtyStamps, 0);
            _syncStamp = 0;
            _steadySpatialDirtyStamp = 0;
            _spatialDirty = true;
            _smartStopDirty = true;
        }

        public void Dispose()
        {
            EntityIds.Dispose();
            EntityToAgentIndex.Dispose();
            Positions.Dispose();
            Velocities.Dispose();
            Radii.Dispose();
            GoalPositions.Dispose();
            GoalRadii.Dispose();
            GoalDistances.Dispose();
            HasPointGoals.Dispose();
            SmartStopFlags.Dispose();
            SpatialDirtyAgentIndices.Dispose();
            _seenSyncStamps.Dispose();
            _steadySpatialDirtyStamps.Dispose();
        }

        private bool RemoveMissingAgents()
        {
            bool removedAny = false;
            for (int index = Count - 1; index >= 0; index--)
            {
                if (_seenSyncStamps[index] == _syncStamp)
                {
                    continue;
                }

                RemoveAtSwapBack(index);
                removedAny = true;
            }

            return removedAny;
        }

        private void RemoveAtSwapBack(int index)
        {
            int lastIndex = Count - 1;
            int removedEntityId = EntityIds[index];
            if ((uint)removedEntityId < (uint)EntityToAgentIndex.Length)
            {
                EntityToAgentIndex[removedEntityId] = -1;
            }

            if (index != lastIndex)
            {
                int movedEntityId = EntityIds[lastIndex];

                EntityIds[index] = EntityIds[lastIndex];
                Positions[index] = Positions[lastIndex];
                Velocities[index] = Velocities[lastIndex];
                Radii[index] = Radii[lastIndex];
                GoalPositions[index] = GoalPositions[lastIndex];
                GoalRadii[index] = GoalRadii[lastIndex];
                GoalDistances[index] = GoalDistances[lastIndex];
                HasPointGoals[index] = HasPointGoals[lastIndex];
                SmartStopFlags[index] = SmartStopFlags[lastIndex];
                _seenSyncStamps[index] = _seenSyncStamps[lastIndex];

                if ((uint)movedEntityId < (uint)EntityToAgentIndex.Length)
                {
                    EntityToAgentIndex[movedEntityId] = index;
                }
            }

            EntityIds.RemoveAt(lastIndex);
            Positions.RemoveAt(lastIndex);
            Velocities.RemoveAt(lastIndex);
            Radii.RemoveAt(lastIndex);
            GoalPositions.RemoveAt(lastIndex);
            GoalRadii.RemoveAt(lastIndex);
            GoalDistances.RemoveAt(lastIndex);
            HasPointGoals.RemoveAt(lastIndex);
            SmartStopFlags.RemoveAt(lastIndex);
            _seenSyncStamps.RemoveAt(lastIndex);
        }

        private void EnsureEntityIndexCapacity(int entityId)
        {
            if ((uint)entityId < (uint)EntityToAgentIndex.Length)
            {
                return;
            }

            int oldLength = EntityToAgentIndex.Length;
            int newLength = Math.Max(8, oldLength);
            while (newLength <= entityId)
            {
                newLength *= 2;
            }

            EntityToAgentIndex = UnsafeArray.Resize(ref EntityToAgentIndex, newLength);
            EntityToAgentIndex.AsSpan().Slice(oldLength, newLength - oldLength).Fill(-1);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void MarkSteadySpatialDirtyAgent(int agentIndex)
        {
            _steadySpatialDirtyStamps[agentIndex] = _steadySpatialDirtyStamp;
        }

        private void CollectSteadySpatialDirtyAgentIndices()
        {
            SpatialDirtyAgentIndices.Clear();
            for (int i = 0; i < Count; i++)
            {
                if (_steadySpatialDirtyStamps[i] == _steadySpatialDirtyStamp)
                {
                    SpatialDirtyAgentIndices.Add(i);
                }
            }
        }
    }
}
