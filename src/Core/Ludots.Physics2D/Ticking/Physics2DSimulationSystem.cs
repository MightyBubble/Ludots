using System;
using System.Diagnostics;
using Arch.Core;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Engine.Physics2D;
using Ludots.Core.Engine.TimeFlow;
using Ludots.Core.Physics2D.Components;
using Ludots.Core.Physics2D.Systems;

namespace Ludots.Core.Physics2D.Ticking
{
    public sealed class Physics2DSimulationSystem : ISystem<float>
    {
        public bool Enabled { get; set; } = true;

        public BuildPhysicsWorldSystem2D Build { get; }
        public AdaptiveSpatialSystem2D Spatial { get; }
        
        /// <summary>
        /// Interpolation alpha [0, 1] for smooth visual rendering.
        /// Should be read by visual sync systems after physics update.
        /// </summary>
        public float InterpolationAlpha => _distributor?.InterpolationAlpha ?? 1f;

        private readonly World _world;
        private readonly IClock _clock;
        private readonly Physics2DTickPolicy _tickPolicy;
        private readonly Physics2DBroadphasePolicy _broadphasePolicy;
        private Entity _statsEntity;
        private Entity _runtimeStateEntity;
        private QueryDescription _activePairsQuery;
        private QueryDescription _awakeDynamicBodiesQuery;
        private readonly Stopwatch _stopwatch = new Stopwatch();

        private readonly Physics2DPipelineDefinition _pipeline;

        private int _cachedPolicyVersion;
        private int _fixedHz;
        private int _physicsHz;
        private DiscreteRateTickDistributor? _distributor;

        public Physics2DSimulationSystem(
            World world,
            IClock clock,
            Physics2DTickPolicy tickPolicy,
            Physics2DSolverConfig solverConfig,
            ShapeDataStorage2D shapeStorage,
            KinematicTargetPoseBuffer2D kinematicPoses)
            : this(
                world,
                clock,
                tickPolicy,
                solverConfig,
                shapeStorage,
                new Physics2DBroadphasePolicy(new Physics2DBroadphaseConfig()),
                kinematicPoses)
        {
        }

        public Physics2DSimulationSystem(
            World world,
            IClock clock,
            Physics2DTickPolicy tickPolicy,
            Physics2DSolverConfig solverConfig,
            ShapeDataStorage2D shapeStorage,
            Physics2DBroadphasePolicy broadphasePolicy,
            KinematicTargetPoseBuffer2D kinematicPoses)
        {
            _world = world;
            _clock = clock;
            _tickPolicy = tickPolicy;

            ArgumentNullException.ThrowIfNull(solverConfig);
            ArgumentNullException.ThrowIfNull(shapeStorage);
            ArgumentNullException.ThrowIfNull(broadphasePolicy);
            ArgumentNullException.ThrowIfNull(kinematicPoses);

            _broadphasePolicy = broadphasePolicy;
            KinematicPoses = kinematicPoses;
            _pipeline = Physics2DPipelineFactory.CreateProduction(world, solverConfig, tickPolicy, shapeStorage, kinematicPoses);
            Build = _pipeline.Build;
            Spatial = _pipeline.Spatial;
        }

        /// <summary>
        /// Drive channel for kinematic bodies: submit one target pose per entity per fixed step.
        /// </summary>
        public KinematicTargetPoseBuffer2D KinematicPoses { get; }

        public ReadOnlySpan<string> PipelineStepNames => _pipeline.StepNames;

        public void Initialize()
        {
            _statsEntity = _world.Create(new Physics2DPerfStats());
            _activePairsQuery = new QueryDescription().WithAll<CollisionPair, ActiveCollisionPairTag>();
            _runtimeStateEntity = _world.Create(new Physics2DRuntimeState());
            _awakeDynamicBodiesQuery = new QueryDescription().WithAll<Mass2D>().WithNone<SleepingTag>();

            ReadOnlySpan<ISystem<float>> systems = _pipeline.Systems;
            for (int i = 0; i < systems.Length; i++)
            {
                systems[i].Initialize();
            }
        }

        public void BeforeUpdate(in float t)
        {
        }

        public void Update(in float fixedDeltaTime)
        {
            if (!Enabled) return;
            if (_tickPolicy.TargetHz == 0) return;
            if (fixedDeltaTime <= 0f) return;

            EnsureSchedulerInitialized(fixedDeltaTime);

            var distributor = _distributor ?? throw new InvalidOperationException("Physics2D tick distributor is not initialized.");
            int stepsToRun = distributor.NextStepCount();
            float physicsDt = distributor.TargetDeltaTime;

            _stopwatch.Restart();
            for (int i = 0; i < stepsToRun; i++)
            {
                StepOnce(physicsDt);
                _clock.Advance(ClockDomainId.PhysicsStep, ticks: 1);
            }
            _stopwatch.Stop();

            var pairStatsJob = new CollisionPairStatsJob();
            _world.InlineQuery<CollisionPairStatsJob, CollisionPair>(in _activePairsQuery, ref pairStatsJob);

            var awakeDynamicBodiesJob = new AwakeDynamicBodiesJob();
            _world.InlineQuery<AwakeDynamicBodiesJob, Mass2D>(in _awakeDynamicBodiesQuery, ref awakeDynamicBodiesJob);

            _world.Set(_runtimeStateEntity, new Physics2DRuntimeState 
            { 
                AnyAwakeDynamicBodies = awakeDynamicBodiesJob.AnyAwakeDynamicBodies,
                LastPhysicsStepTime = stepsToRun > 0 ? Time.FixedTotalTime : _world.Get<Physics2DRuntimeState>(_runtimeStateEntity).LastPhysicsStepTime,
                PhysicsStepDuration = physicsDt,
                InterpolationAlpha = InterpolationAlpha  // 从 DiscreteRateTickDistributor 获取的物理帧 alpha
            });

            int fixedHz = FixedHzFromDeltaTime(fixedDeltaTime);
            var stats = new Physics2DPerfStats
            {
                FixedHz = fixedHz,
                PhysicsHz = _tickPolicy.TargetHz,
                PhysicsStepsLastFixedTick = stepsToRun,
                PhysicsUpdateMs = _stopwatch.Elapsed.TotalMilliseconds,
                PotentialPairs = pairStatsJob.PotentialPairs,
                ContactPairs = pairStatsJob.ContactPairs,
                DynamicBodies = Build.DynamicRigidBodyDescriptors.Count,
                StaticBodies = Build.StaticRigidBodyDescriptors.Count,
                DirtyStaticBodies = Build.DirtyStaticBodyCountLastUpdate,
                BroadphaseStrategy = (int)Spatial.CurrentStrategyKind,
                BroadphaseCellSizeCm = Spatial.CurrentCellSizeCm,
                DroppedPairs = Spatial.DroppedPairsLastUpdate
            };
            _world.Set(_statsEntity, stats);
        }

        public void AfterUpdate(in float t)
        {
        }

        public void Dispose()
        {
        }

        private struct CollisionPairStatsJob : IForEach<CollisionPair>
        {
            public int PotentialPairs;
            public int ContactPairs;

            public void Update(ref CollisionPair pair)
            {
                PotentialPairs++;
                if (pair.ContactCount > 0)
                {
                    ContactPairs++;
                }
            }
        }

        private struct AwakeDynamicBodiesJob : IForEach<Mass2D>
        {
            public bool AnyAwakeDynamicBodies;

            public void Update(ref Mass2D mass)
            {
                if (!AnyAwakeDynamicBodies && mass.IsDynamic)
                {
                    AnyAwakeDynamicBodies = true;
                }
            }
        }

        private void StepOnce(float dt)
        {
            Spatial.ApplyBroadphasePolicy(_broadphasePolicy);
            ReadOnlySpan<ISystem<float>> systems = _pipeline.Systems;
            for (int i = 0; i < systems.Length; i++)
            {
                systems[i].Update(dt);
            }
        }

        private void EnsureSchedulerInitialized(float fixedDeltaTime)
        {
            int currentVersion = _tickPolicy.Version;
            int fixedHz = FixedHzFromDeltaTime(fixedDeltaTime);
            int physicsHz = _tickPolicy.TargetHz;

            if (_cachedPolicyVersion == currentVersion && _fixedHz == fixedHz && _physicsHz == physicsHz) return;

            if (physicsHz < 0) throw new InvalidOperationException("Physics2DTickPolicy.TargetHz must be >= 0.");

            _cachedPolicyVersion = currentVersion;
            _fixedHz = fixedHz;
            _physicsHz = physicsHz;

            if (_distributor == null)
            {
                _distributor = new DiscreteRateTickDistributor(fixedHz, physicsHz, _tickPolicy.MaxStepsPerFixedTick);
            }
            else
            {
                _distributor.Reset(fixedHz, physicsHz, _tickPolicy.MaxStepsPerFixedTick);
            }
        }

        private static int FixedHzFromDeltaTime(float fixedDeltaTime)
        {
            if (!(fixedDeltaTime > 0f)) throw new ArgumentOutOfRangeException(nameof(fixedDeltaTime));

            float rawHz = 1f / fixedDeltaTime;
            int hz = (int)MathF.Round(rawHz);
            if (hz <= 0) throw new InvalidOperationException("FixedDeltaTime must map to a positive integer Hz.");

            float reconstructedDt = 1f / hz;
            float error = MathF.Abs(reconstructedDt - fixedDeltaTime);
            if (error > 1e-5f)
            {
                throw new InvalidOperationException($"FixedDeltaTime={fixedDeltaTime} is not representable as 1/integer Hz (closest {hz}Hz gives {reconstructedDt}, error {error}).");
            }

            return hz;
        }
    }
}
