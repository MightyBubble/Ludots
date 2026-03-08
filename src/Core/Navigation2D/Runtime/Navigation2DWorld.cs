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
        public UnsafeList<float> MaxSpeeds;
        public UnsafeList<float> MaxAccels;
        public UnsafeList<float> NeighborDistances;
        public UnsafeList<float> TimeHorizons;
        public UnsafeList<int> MaxNeighborCounts;
        public UnsafeList<Vector2> CachedSteeringDesiredVelocities;
        public UnsafeList<Vector2> CachedSteeringPreferredVelocities;
        public UnsafeList<Vector2> CachedSteeringVelocities;
        public UnsafeList<Vector2> CachedSteeringPositions;
        public UnsafeList<uint> CachedSteeringNeighborSignatures;
        public UnsafeList<int> CachedSteeringNeighborCounts;
        public UnsafeList<int> CachedSteeringTicks;
        public UnsafeList<byte> CachedSteeringValid;

        private UnsafeList<int> _seenSyncStamps;
        private UnsafeArray<int> _steadySpatialDirtyStamps;
        private int _syncStamp;
        private int _steadySpatialDirtyStamp;
        private bool _spatialDirty;
        private bool _smartStopDirty;
        private int _steadySpatialDirty;
        private int _steadySmartStopDirty;
        private int _steadyFallbackRequired;
        private int _steeringFrameTick;
        private int _steeringCacheFrameEnabled;
        private int _steeringCacheStableWorldFrame;
        private int _steeringCacheLookupsFrame;
        private int _steeringCacheHitsFrame;
        private int _steeringCacheStoresFrame;
        private long _steeringCacheLookupsTotal;
        private long _steeringCacheHitsTotal;
        private long _steeringCacheStoresTotal;

        public int Count => Positions.Count;
        public int SteeringFrameTick => Volatile.Read(ref _steeringFrameTick);
        public bool SteeringCacheFrameEnabled => Volatile.Read(ref _steeringCacheFrameEnabled) != 0;
        public bool SteeringCacheStableWorldFrame => Volatile.Read(ref _steeringCacheStableWorldFrame) != 0;
        public int SteeringCacheLookupsFrame => Volatile.Read(ref _steeringCacheLookupsFrame);
        public int SteeringCacheHitsFrame => Volatile.Read(ref _steeringCacheHitsFrame);
        public int SteeringCacheStoresFrame => Volatile.Read(ref _steeringCacheStoresFrame);
        public long SteeringCacheLookupsTotal => Interlocked.Read(ref _steeringCacheLookupsTotal);
        public long SteeringCacheHitsTotal => Interlocked.Read(ref _steeringCacheHitsTotal);
        public long SteeringCacheStoresTotal => Interlocked.Read(ref _steeringCacheStoresTotal);

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
            MaxSpeeds = new UnsafeList<float>(settings.MaxAgents);
            MaxAccels = new UnsafeList<float>(settings.MaxAgents);
            NeighborDistances = new UnsafeList<float>(settings.MaxAgents);
            TimeHorizons = new UnsafeList<float>(settings.MaxAgents);
            MaxNeighborCounts = new UnsafeList<int>(settings.MaxAgents);
            CachedSteeringDesiredVelocities = new UnsafeList<Vector2>(settings.MaxAgents);
            CachedSteeringPreferredVelocities = new UnsafeList<Vector2>(settings.MaxAgents);
            CachedSteeringVelocities = new UnsafeList<Vector2>(settings.MaxAgents);
            CachedSteeringPositions = new UnsafeList<Vector2>(settings.MaxAgents);
            CachedSteeringNeighborSignatures = new UnsafeList<uint>(settings.MaxAgents);
            CachedSteeringNeighborCounts = new UnsafeList<int>(settings.MaxAgents);
            CachedSteeringTicks = new UnsafeList<int>(settings.MaxAgents);
            CachedSteeringValid = new UnsafeList<byte>(settings.MaxAgents);
            _seenSyncStamps = new UnsafeList<int>(settings.MaxAgents);
            _steadySpatialDirtyStamps = new UnsafeArray<int>(Math.Max(8, settings.MaxAgents));
            _syncStamp = 0;
            _steadySpatialDirtyStamp = 0;
            _steadySpatialDirty = 0;
            _steadySmartStopDirty = 0;
            _steadyFallbackRequired = 0;
            _steeringFrameTick = 0;
            _steeringCacheFrameEnabled = 0;
            _steeringCacheLookupsFrame = 0;
            _steeringCacheHitsFrame = 0;
            _steeringCacheStoresFrame = 0;
            _steeringCacheLookupsTotal = 0;
            _steeringCacheHitsTotal = 0;
            _steeringCacheStoresTotal = 0;
            _spatialDirty = true;
            _smartStopDirty = true;
            _steeringFrameTick = 0;
            _steeringCacheFrameEnabled = 0;
            _steeringCacheLookupsFrame = 0;
            _steeringCacheHitsFrame = 0;
            _steeringCacheStoresFrame = 0;
            _steeringCacheLookupsTotal = 0;
            _steeringCacheHitsTotal = 0;
            _steeringCacheStoresTotal = 0;
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

        public void BeginSteeringFrame(int steeringTick, bool cacheEnabled, bool stableWorldFrame)
        {
            Volatile.Write(ref _steeringFrameTick, steeringTick);
            Volatile.Write(ref _steeringCacheFrameEnabled, cacheEnabled ? 1 : 0);
            Volatile.Write(ref _steeringCacheStableWorldFrame, stableWorldFrame ? 1 : 0);
            Volatile.Write(ref _steeringCacheLookupsFrame, 0);
            Volatile.Write(ref _steeringCacheHitsFrame, 0);
            Volatile.Write(ref _steeringCacheStoresFrame, 0);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RecordSteeringCacheLookup(bool hit)
        {
            Interlocked.Increment(ref _steeringCacheLookupsFrame);
            Interlocked.Increment(ref _steeringCacheLookupsTotal);
            if (hit)
            {
                Interlocked.Increment(ref _steeringCacheHitsFrame);
                Interlocked.Increment(ref _steeringCacheHitsTotal);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RecordSteeringCacheStore()
        {
            Interlocked.Increment(ref _steeringCacheStoresFrame);
            Interlocked.Increment(ref _steeringCacheStoresTotal);
        }

        public void UpdateExistingAgent(
            int agentIndex,
            in Vector2 position,
            in Vector2 velocity,
            float radius,
            float maxSpeed,
            float maxAccel,
            float neighborDistance,
            float timeHorizon,
            int maxNeighbors,
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
                Radii[agentIndex] != radius ||
                MaxSpeeds[agentIndex] != maxSpeed ||
                MaxAccels[agentIndex] != maxAccel ||
                NeighborDistances[agentIndex] != neighborDistance ||
                TimeHorizons[agentIndex] != timeHorizon ||
                MaxNeighborCounts[agentIndex] != maxNeighbors ||
                GoalPositions[agentIndex] != goalPosition ||
                GoalRadii[agentIndex] != goalRadius ||
                GoalDistances[agentIndex] != goalDistance ||
                HasPointGoals[agentIndex] != hasPointGoalByte)
            {
                Interlocked.Exchange(ref _steadySmartStopDirty, 1);
            }

            Velocities[agentIndex] = velocity;
            Radii[agentIndex] = radius;
            MaxSpeeds[agentIndex] = maxSpeed;
            MaxAccels[agentIndex] = maxAccel;
            NeighborDistances[agentIndex] = neighborDistance;
            TimeHorizons[agentIndex] = timeHorizon;
            MaxNeighborCounts[agentIndex] = maxNeighbors;
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
            float maxSpeed,
            float maxAccel,
            float neighborDistance,
            float timeHorizon,
            int maxNeighbors,
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
                MaxSpeeds.Add(maxSpeed);
                MaxAccels.Add(maxAccel);
                NeighborDistances.Add(neighborDistance);
                TimeHorizons.Add(timeHorizon);
                MaxNeighborCounts.Add(maxNeighbors);
                GoalPositions.Add(goalPosition);
                GoalRadii.Add(goalRadius);
                GoalDistances.Add(goalDistance);
                HasPointGoals.Add(hasPointGoalByte);
                SmartStopFlags.Add(0);
                SpatialDirtyAgentIndices.Add(agentIndex);
                CachedSteeringDesiredVelocities.Add(Vector2.Zero);
                CachedSteeringPreferredVelocities.Add(Vector2.Zero);
                CachedSteeringVelocities.Add(Vector2.Zero);
                CachedSteeringPositions.Add(Vector2.Zero);
                CachedSteeringNeighborSignatures.Add(0u);
                CachedSteeringNeighborCounts.Add(0);
                CachedSteeringTicks.Add(int.MinValue);
                CachedSteeringValid.Add(0);

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
                Radii[agentIndex] != radius ||
                MaxSpeeds[agentIndex] != maxSpeed ||
                MaxAccels[agentIndex] != maxAccel ||
                NeighborDistances[agentIndex] != neighborDistance ||
                TimeHorizons[agentIndex] != timeHorizon ||
                MaxNeighborCounts[agentIndex] != maxNeighbors ||
                GoalPositions[agentIndex] != goalPosition ||
                GoalRadii[agentIndex] != goalRadius ||
                GoalDistances[agentIndex] != goalDistance ||
                HasPointGoals[agentIndex] != hasPointGoalByte)
            {
                _smartStopDirty = true;
            }

            Velocities[agentIndex] = velocity;
            Radii[agentIndex] = radius;
            MaxSpeeds[agentIndex] = maxSpeed;
            MaxAccels[agentIndex] = maxAccel;
            NeighborDistances[agentIndex] = neighborDistance;
            TimeHorizons[agentIndex] = timeHorizon;
            MaxNeighborCounts[agentIndex] = maxNeighbors;
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
            MaxSpeeds.Clear();
            MaxAccels.Clear();
            NeighborDistances.Clear();
            TimeHorizons.Clear();
            MaxNeighborCounts.Clear();
            GoalPositions.Clear();
            GoalRadii.Clear();
            GoalDistances.Clear();
            HasPointGoals.Clear();
            SmartStopFlags.Clear();
            SpatialDirtyAgentIndices.Clear();
            CachedSteeringDesiredVelocities.Clear();
            CachedSteeringPreferredVelocities.Clear();
            CachedSteeringVelocities.Clear();
            CachedSteeringPositions.Clear();
            CachedSteeringNeighborSignatures.Clear();
            CachedSteeringNeighborCounts.Clear();
            CachedSteeringTicks.Clear();
            CachedSteeringValid.Clear();
            _seenSyncStamps.Clear();
            UnsafeArray.Fill(ref _steadySpatialDirtyStamps, 0);
            _syncStamp = 0;
            _steadySpatialDirtyStamp = 0;
            _steadySpatialDirty = 0;
            _steadySmartStopDirty = 0;
            _steadyFallbackRequired = 0;
            _steeringFrameTick = 0;
            _steeringCacheFrameEnabled = 0;
            _steeringCacheLookupsFrame = 0;
            _steeringCacheHitsFrame = 0;
            _steeringCacheStoresFrame = 0;
            _steeringCacheLookupsTotal = 0;
            _steeringCacheHitsTotal = 0;
            _steeringCacheStoresTotal = 0;
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
            MaxSpeeds.Dispose();
            MaxAccels.Dispose();
            NeighborDistances.Dispose();
            TimeHorizons.Dispose();
            MaxNeighborCounts.Dispose();
            GoalPositions.Dispose();
            GoalRadii.Dispose();
            GoalDistances.Dispose();
            HasPointGoals.Dispose();
            SmartStopFlags.Dispose();
            SpatialDirtyAgentIndices.Dispose();
            CachedSteeringDesiredVelocities.Dispose();
            CachedSteeringPreferredVelocities.Dispose();
            CachedSteeringVelocities.Dispose();
            CachedSteeringPositions.Dispose();
            CachedSteeringNeighborSignatures.Dispose();
            CachedSteeringNeighborCounts.Dispose();
            CachedSteeringTicks.Dispose();
            CachedSteeringValid.Dispose();
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
                MaxSpeeds[index] = MaxSpeeds[lastIndex];
                MaxAccels[index] = MaxAccels[lastIndex];
                NeighborDistances[index] = NeighborDistances[lastIndex];
                TimeHorizons[index] = TimeHorizons[lastIndex];
                MaxNeighborCounts[index] = MaxNeighborCounts[lastIndex];
                GoalPositions[index] = GoalPositions[lastIndex];
                GoalRadii[index] = GoalRadii[lastIndex];
                GoalDistances[index] = GoalDistances[lastIndex];
                HasPointGoals[index] = HasPointGoals[lastIndex];
                SmartStopFlags[index] = SmartStopFlags[lastIndex];
                CachedSteeringDesiredVelocities[index] = CachedSteeringDesiredVelocities[lastIndex];
                CachedSteeringPreferredVelocities[index] = CachedSteeringPreferredVelocities[lastIndex];
                CachedSteeringVelocities[index] = CachedSteeringVelocities[lastIndex];
                CachedSteeringPositions[index] = CachedSteeringPositions[lastIndex];
                CachedSteeringNeighborSignatures[index] = CachedSteeringNeighborSignatures[lastIndex];
                CachedSteeringNeighborCounts[index] = CachedSteeringNeighborCounts[lastIndex];
                CachedSteeringTicks[index] = CachedSteeringTicks[lastIndex];
                CachedSteeringValid[index] = CachedSteeringValid[lastIndex];
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
            MaxSpeeds.RemoveAt(lastIndex);
            MaxAccels.RemoveAt(lastIndex);
            NeighborDistances.RemoveAt(lastIndex);
            TimeHorizons.RemoveAt(lastIndex);
            MaxNeighborCounts.RemoveAt(lastIndex);
            GoalPositions.RemoveAt(lastIndex);
            GoalRadii.RemoveAt(lastIndex);
            GoalDistances.RemoveAt(lastIndex);
            HasPointGoals.RemoveAt(lastIndex);
            SmartStopFlags.RemoveAt(lastIndex);
            CachedSteeringDesiredVelocities.RemoveAt(lastIndex);
            CachedSteeringPreferredVelocities.RemoveAt(lastIndex);
            CachedSteeringVelocities.RemoveAt(lastIndex);
            CachedSteeringPositions.RemoveAt(lastIndex);
            CachedSteeringNeighborSignatures.RemoveAt(lastIndex);
            CachedSteeringNeighborCounts.RemoveAt(lastIndex);
            CachedSteeringTicks.RemoveAt(lastIndex);
            CachedSteeringValid.RemoveAt(lastIndex);
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
