using System;
using System.Diagnostics;
using Arch.Core;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Engine.Navigation2D;
using Ludots.Core.Engine.Physics2D;
using Ludots.Core.Navigation2D.Components;
using Ludots.Core.Navigation2D.Runtime;

namespace Ludots.Core.Physics2D.Systems
{
    public sealed class Navigation2DSimulationSystem2D : ISystem<float>
    {
        public bool Enabled { get; set; } = true;

        private readonly World _world;
        private readonly Navigation2DRuntime _runtime;
        private readonly IClock _clock;
        private readonly Navigation2DTickPolicy _tickPolicy;
        private readonly Navigation2DSteeringSystem2D _steering;
        private readonly SimulationTimingSnapshot? _timingSnapshot;
        private readonly Stopwatch _stopwatch = new();
        private Entity _statsEntity;
        private QueryDescription _agentQuery;
        private QueryDescription _groupQuery;

        private int _cachedPolicyVersion;
        private int _fixedHz;
        private int _navHz;
        private DiscreteRateTickDistributor? _distributor;

        public Navigation2DSimulationSystem2D(World world, Navigation2DRuntime runtime, IClock clock, Navigation2DTickPolicy tickPolicy, SimulationTimingSnapshot? timingSnapshot = null)
        {
            _world = world;
            _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _tickPolicy = tickPolicy ?? throw new ArgumentNullException(nameof(tickPolicy));
            _steering = new Navigation2DSteeringSystem2D(world, runtime);
            _timingSnapshot = timingSnapshot;
        }

        public void Initialize()
        {
            _statsEntity = _world.Create(new Navigation2DPerfStats());
            _agentQuery = new QueryDescription().WithAll<NavActor>();
            _groupQuery = new QueryDescription().WithAll<NavGroupTag>();
        }

        public void BeforeUpdate(in float t)
        {
        }

        public void Update(in float fixedDeltaTime)
        {
            if (!Enabled) return;
            if (_tickPolicy.TargetHz == 0) return;

            EnsureSchedulerInitialized(fixedDeltaTime);

            var distributor = _distributor ?? throw new InvalidOperationException("Navigation2D tick distributor is not initialized.");
            int stepsToRun = distributor.NextStepCount();
            float navDt = distributor.TargetDeltaTime;

            _stopwatch.Restart();
            for (int i = 0; i < stepsToRun; i++)
            {
                _steering.Update(navDt);
                _clock.Advance(ClockDomainId.NavigationStep, ticks: 1);
            }
            _stopwatch.Stop();

            _world.Set(_statsEntity, new Navigation2DPerfStats
            {
                FixedHz = FixedHzFromDeltaTime(fixedDeltaTime),
                NavigationHz = _tickPolicy.TargetHz,
                NavigationStepsLastFixedTick = stepsToRun,
                NavigationUpdateMs = _stopwatch.Elapsed.TotalMilliseconds,
                ActiveAgents = _world.CountEntities(in _agentQuery),
                ActiveGroups = _world.CountEntities(in _groupQuery),
            });

            if (_timingSnapshot != null)
            {
                _timingSnapshot.FixedHz = FixedHzFromDeltaTime(fixedDeltaTime);
                _timingSnapshot.NavigationHz = _tickPolicy.TargetHz;
                _timingSnapshot.NavigationStepsLastFixedTick = stepsToRun;
                _timingSnapshot.NavigationMs = _stopwatch.Elapsed.TotalMilliseconds;
            }
        }

        public void AfterUpdate(in float t)
        {
        }

        public void Dispose()
        {
        }

        private void EnsureSchedulerInitialized(float fixedDeltaTime)
        {
            int currentVersion = _tickPolicy.Version;
            int fixedHz = FixedHzFromDeltaTime(fixedDeltaTime);
            int navHz = _tickPolicy.TargetHz;

            if (_cachedPolicyVersion == currentVersion && _fixedHz == fixedHz && _navHz == navHz) return;

            if (navHz < 0) throw new InvalidOperationException("Navigation2DTickPolicy.TargetHz must be >= 0.");

            _cachedPolicyVersion = currentVersion;
            _fixedHz = fixedHz;
            _navHz = navHz;

            if (_distributor == null)
            {
                _distributor = new DiscreteRateTickDistributor(fixedHz, navHz, _tickPolicy.MaxStepsPerFixedTick);
            }
            else
            {
                _distributor.Reset(fixedHz, navHz, _tickPolicy.MaxStepsPerFixedTick);
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
