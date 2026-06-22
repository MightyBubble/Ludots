using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Arch.Buffer;
using Arch.Core;
using Arch.System;
using Ludots.Core.Engine.Physics2D;
using Ludots.Core.Physics2D.Components;

namespace Ludots.Core.Physics2D.Systems
{
    public sealed class SleepingSystem : BaseSystem<World, float>
    {
        private readonly struct SleepCandidate
        {
            public readonly Entity Entity;
            public readonly int IslandId;

            public SleepCandidate(Entity entity, int islandId)
            {
                Entity = entity;
                IslandId = islandId;
            }
        }

        private struct IslandAccumulator
        {
            public int IslandId;
            public int EntityCount;
            public int ReadyCount;
        }

        private readonly Dictionary<int, int> _islandToAccumulatorIndex = new();
        private readonly List<IslandAccumulator> _islandAccumulators = new();
        private readonly List<SleepCandidate> _sleepCandidates = new();
        private readonly HashSet<int> _islandsToWake = new();
        private readonly CommandBuffer _commandBuffer = new();
        private readonly Physics2DSolverConfig _config;
        private readonly Physics2DTickPolicy _tickPolicy;

        private readonly QueryDescription _activeEntitiesQuery;
        private readonly QueryDescription _sleepingEntitiesQuery;
        private readonly QueryDescription _collisionPairQuery;

        public SleepingSystem(World world, Physics2DSolverConfig config, Physics2DTickPolicy tickPolicy) : base(world)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _tickPolicy = tickPolicy ?? throw new ArgumentNullException(nameof(tickPolicy));
            _activeEntitiesQuery = new QueryDescription().WithAll<Island, Motion, Mass2D>().WithNone<SleepingTag>();
            _sleepingEntitiesQuery = new QueryDescription().WithAll<Island, SleepingTag>();
            _collisionPairQuery = new QueryDescription().WithAll<CollisionPair, ActiveCollisionPairTag>();
        }

        public override void Update(in float deltaTime)
        {
            _islandToAccumulatorIndex.Clear();
            _islandAccumulators.Clear();
            _sleepCandidates.Clear();

            var collectJob = new CollectActiveIslandEntitiesJob
            {
                IslandToAccumulatorIndex = _islandToAccumulatorIndex,
                IslandAccumulators = _islandAccumulators,
                SleepCandidates = _sleepCandidates,
                SleepFrameThreshold = SleepFrameThreshold
            };
            World.InlineEntityQuery<CollectActiveIslandEntitiesJob, Island, Motion, Mass2D>(in _activeEntitiesQuery, ref collectJob);

            for (int i = 0; i < _sleepCandidates.Count; i++)
            {
                SleepCandidate candidate = _sleepCandidates[i];
                if (!_islandToAccumulatorIndex.TryGetValue(candidate.IslandId, out int accumulatorIndex))
                {
                    continue;
                }

                IslandAccumulator accumulator = _islandAccumulators[accumulatorIndex];
                if (accumulator.EntityCount != accumulator.ReadyCount)
                {
                    continue;
                }

                if (World.IsAlive(candidate.Entity))
                {
                    _commandBuffer.Add(candidate.Entity, new SleepingTag());
                }
            }

            _islandsToWake.Clear();

            var wakeIslandJob = new CollectWakeIslandsJob
            {
                IslandsToWake = _islandsToWake
            };
            World.InlineQuery<CollectWakeIslandsJob, CollisionPair>(in _collisionPairQuery, ref wakeIslandJob);

            if (_islandsToWake.Count > 0)
            {
                foreach (ref var chunk in World.Query(in _sleepingEntitiesQuery))
                {
                    var wakeEntitiesJob = new WakeSleepingEntitiesChunkJob
                    {
                        World = World,
                        CommandBuffer = _commandBuffer,
                        IslandsToWake = _islandsToWake
                    };
                    wakeEntitiesJob.Execute(ref chunk);
                }
            }

            if (_commandBuffer.Size > 0)
            {
                _commandBuffer.Playback(World);
            }
        }

        private int SleepFrameThreshold
        {
            get
            {
                if (_config.SleepTimeSeconds <= 0f)
                {
                    return 0;
                }

                int physicsHz = Math.Max(0, _tickPolicy.TargetHz);
                if (physicsHz == 0)
                {
                    return int.MaxValue;
                }

                return Math.Max(1, (int)MathF.Ceiling(_config.SleepTimeSeconds * physicsHz));
            }
        }

        private struct CollectActiveIslandEntitiesJob : IForEachWithEntity<Island, Motion, Mass2D>
        {
            public Dictionary<int, int> IslandToAccumulatorIndex;
            public List<IslandAccumulator> IslandAccumulators;
            public List<SleepCandidate> SleepCandidates;
            public int SleepFrameThreshold;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Update(Entity entity, ref Island island, ref Motion motion, ref Mass2D mass)
            {
                if (mass.IsStatic) return;

                if (!IslandToAccumulatorIndex.TryGetValue(island.IslandId, out int accumulatorIndex))
                {
                    accumulatorIndex = IslandAccumulators.Count;
                    IslandToAccumulatorIndex[island.IslandId] = accumulatorIndex;
                    IslandAccumulators.Add(new IslandAccumulator
                    {
                        IslandId = island.IslandId,
                        EntityCount = 0,
                        ReadyCount = 0
                    });
                }

                IslandAccumulator accumulator = IslandAccumulators[accumulatorIndex];
                accumulator.EntityCount++;
                if (motion.SleepTimer >= SleepFrameThreshold)
                {
                    accumulator.ReadyCount++;
                    SleepCandidates.Add(new SleepCandidate(entity, island.IslandId));
                }

                IslandAccumulators[accumulatorIndex] = accumulator;
            }
        }

        private struct CollectWakeIslandsJob : IForEach<CollisionPair>
        {
            public HashSet<int> IslandsToWake;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Update(ref CollisionPair pair)
            {
                if (pair.ContactCount == 0) return;

                bool isASleeping = pair.IsSleepingA != 0;
                bool isBSleeping = pair.IsSleepingB != 0;

                if (isASleeping && isBSleeping)
                {
                    return;
                }

                if (!isASleeping && isBSleeping && pair.IslandB >= 0)
                {
                    IslandsToWake.Add(pair.IslandB);
                }
                else if (isASleeping && !isBSleeping && pair.IslandA >= 0)
                {
                    IslandsToWake.Add(pair.IslandA);
                }
            }
        }

        private struct WakeSleepingEntitiesChunkJob
        {
            public World World;
            public CommandBuffer CommandBuffer;
            public HashSet<int> IslandsToWake;

            public void Execute(ref Chunk chunk)
            {
                if (chunk.Count <= 0)
                {
                    return;
                }

                var islands = chunk.GetSpan<Island>();
                bool hasMotion = chunk.Has<Motion>();
                Span<Motion> motions = hasMotion ? chunk.GetSpan<Motion>() : default;
                ref Entity entityFirst = ref chunk.Entity(0);

                foreach (int index in chunk)
                {
                    if (!IslandsToWake.Contains(islands[index].IslandId)) continue;

                    Entity entity = Unsafe.Add(ref entityFirst, index);
                    CommandBuffer.Remove<SleepingTag>(in entity);

                    if (hasMotion)
                    {
                        motions[index].SleepTimer = 0;
                    }
                }
            }
        }

        public override void Dispose()
        {
            _commandBuffer.Dispose();
            base.Dispose();
        }
    }
}
