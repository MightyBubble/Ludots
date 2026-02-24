using System;
using System.Diagnostics;
using Arch.Core;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Engine.Physics2D;
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
        private Entity _statsEntity;
        private Entity _runtimeStateEntity;
        private QueryDescription _activePairsQuery;
        private QueryDescription _awakeDynamicBodiesQuery;
        private readonly Stopwatch _stopwatch = new Stopwatch();

        private readonly NarrowPhaseSystem2D _narrowPhase;
        private readonly SolverSystem2D _solver;
        private readonly ApplyImpulsesSystem2D _applyImpulses;
        private readonly PositionCorrectionSystem2D _positionCorrection;
        private readonly FieldDetectorSystem _fieldDetector;
        private readonly IntegrationSystem2D _integration;
        private readonly UpdateMotionSystem _updateMotion;
        private readonly BuildIslandsSystem _buildIslands;
        private readonly SleepingSystem _sleeping;
        private readonly CleanupSystem2D _cleanup;

        private int _cachedPolicyVersion;
        private int _fixedHz;
        private int _physicsHz;
        private DiscreteRateTickDistributor? _distributor;

        public Physics2DSimulationSystem(World world, IClock clock, Physics2DTickPolicy tickPolicy)
        {
            _world = world;
            _clock = clock;
            _tickPolicy = tickPolicy;

            Build = new BuildPhysicsWorldSystem2D(world);
            Spatial = new AdaptiveSpatialSystem2D(world, Build);
            _narrowPhase = new NarrowPhaseSystem2D(world);
            _solver = new SolverSystem2D(world);
            _applyImpulses = new ApplyImpulsesSystem2D(world);
            _positionCorrection = new PositionCorrectionSystem2D(world);
            _fieldDetector = new FieldDetectorSystem(world);
            _integration = new IntegrationSystem2D(world);
            _updateMotion = new UpdateMotionSystem(world);
            _buildIslands = new BuildIslandsSystem(world);
            _sleeping = new SleepingSystem(world);
            _cleanup = new CleanupSystem2D(world);
        }

        public void Initialize()
        {
            _statsEntity = _world.Create(new Physics2DPerfStats());
            _activePairsQuery = new QueryDescription().WithAll<CollisionPair, ActiveCollisionPairTag>();
            _runtimeStateEntity = _world.Create(new Physics2DRuntimeState());
            _awakeDynamicBodiesQuery = new QueryDescription().WithAll<Mass2D>().WithNone<SleepingTag>();

            Build.Initialize();
            Spatial.Initialize();
            _narrowPhase.Initialize();
            _solver.Initialize();
            _applyImpulses.Initialize();
            _positionCorrection.Initialize();
            _fieldDetector.Initialize();
            _integration.Initialize();
            _updateMotion.Initialize();
            _buildIslands.Initialize();
            _sleeping.Initialize();
            _cleanup.Initialize();
        }

        public void BeforeUpdate(in float t)
        {
        }

        public void Update(in float fixedDeltaTime)
        {
            if (!Enabled) return;
            if (_tickPolicy.TargetHz == 0) return;

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

            int potentialPairs = 0;
            int contactPairs = 0;
            var chunks = _world.Query(in _activePairsQuery);
            foreach (var chunk in chunks)
            {
                var pairs = chunk.GetArray<CollisionPair>();
                potentialPairs += chunk.Count;
                for (int i = 0; i < chunk.Count; i++)
                {
                    if (pairs[i].ContactCount > 0) contactPairs++;
                }
            }

            bool anyAwakeDynamicBodies = false;
            _world.Query(in _awakeDynamicBodiesQuery, (ref Mass2D mass) =>
            {
                if (mass.IsDynamic) anyAwakeDynamicBodies = true;
            });
            _world.Set(_runtimeStateEntity, new Physics2DRuntimeState 
            { 
                AnyAwakeDynamicBodies = anyAwakeDynamicBodies,
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
                PotentialPairs = potentialPairs,
                ContactPairs = contactPairs
            };
            _world.Set(_statsEntity, stats);
        }

        public void AfterUpdate(in float t)
        {
        }

        public void Dispose()
        {
        }

        private void StepOnce(float dt)
        {
            Build.Update(dt);
            Spatial.Update(dt);
            _narrowPhase.Update(dt);
            _solver.Update(dt);
            _applyImpulses.Update(dt);
            _positionCorrection.Update(dt);
            _fieldDetector.Update(dt);
            _integration.Update(dt);
            _updateMotion.Update(dt);
            _buildIslands.Update(dt);
            _sleeping.Update(dt);
            _cleanup.Update(dt);
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
