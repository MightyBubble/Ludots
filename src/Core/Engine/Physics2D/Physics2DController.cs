using System;
using Arch.Core;
using Ludots.Core.Scripting;

namespace Ludots.Core.Engine.Physics2D
{
    public sealed class Physics2DController
    {
        private readonly World _world;
        private readonly Physics2DTickPolicy _tickPolicy;
        private readonly Func<ScriptContext> _contextFactory;
        private readonly Action<EventKey, ScriptContext> _fireEvent;

        private int _defaultHz;

        private int _runRemainingFixedTicks;
        private bool _runUntilSleeping;
        private int _runMaxFixedTicks;
        private int _runElapsedFixedTicks;
        private bool _runActive;
        private readonly QueryDescription _runtimeStateQuery = new QueryDescription().WithAll<Physics2DRuntimeState>();

        public Physics2DController(
            World world,
            Physics2DTickPolicy tickPolicy,
            int defaultHz,
            Func<ScriptContext> contextFactory,
            Action<EventKey, ScriptContext> fireEvent)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _tickPolicy = tickPolicy ?? throw new ArgumentNullException(nameof(tickPolicy));
            _defaultHz = Math.Max(0, defaultHz);
            _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
            _fireEvent = fireEvent ?? throw new ArgumentNullException(nameof(fireEvent));
        }

        public bool IsEnabled => _tickPolicy.TargetHz > 0;
        public int TargetHz => _tickPolicy.TargetHz;

        public void Enable(int? hz = null)
        {
            int v = hz ?? _defaultHz;
            if (v < 0) throw new ArgumentOutOfRangeException(nameof(hz));
            if (v == 0) v = _defaultHz;
            _tickPolicy.SetTargetHz(v);
            if (hz.HasValue && hz.Value > 0) _defaultHz = hz.Value;

            var ctx = _contextFactory();
            ctx.Set("PhysicsHz", _tickPolicy.TargetHz);
            _fireEvent(GameEvents.Physics2DEnabled, ctx);
        }

        public void Disable()
        {
            _tickPolicy.SetTargetHz(0);
            ClearRunState();

            var ctx = _contextFactory();
            _fireEvent(GameEvents.Physics2DDisabled, ctx);
        }

        public void RunForFixedTicks(int fixedTicks, int? hz = null)
        {
            if (fixedTicks < 1) throw new ArgumentOutOfRangeException(nameof(fixedTicks));
            Enable(hz);
            _runActive = true;
            _runUntilSleeping = false;
            _runRemainingFixedTicks = fixedTicks;
            _runMaxFixedTicks = fixedTicks;
            _runElapsedFixedTicks = 0;

            var ctx = _contextFactory();
            ctx.Set("Mode", "RunForFixedTicks");
            ctx.Set("FixedTicks", fixedTicks);
            ctx.Set("PhysicsHz", _tickPolicy.TargetHz);
            _fireEvent(GameEvents.Physics2DRunStarted, ctx);
        }

        public void RunUntilSleeping(int? hz = null, int maxFixedTicks = 10_000)
        {
            if (maxFixedTicks < 1) throw new ArgumentOutOfRangeException(nameof(maxFixedTicks));
            Enable(hz);
            _runActive = true;
            _runUntilSleeping = true;
            _runRemainingFixedTicks = 0;
            _runMaxFixedTicks = maxFixedTicks;
            _runElapsedFixedTicks = 0;

            var ctx = _contextFactory();
            ctx.Set("Mode", "RunUntilSleeping");
            ctx.Set("MaxFixedTicks", maxFixedTicks);
            ctx.Set("PhysicsHz", _tickPolicy.TargetHz);
            _fireEvent(GameEvents.Physics2DRunStarted, ctx);
        }

        public void AfterPhysicsFixedTick()
        {
            if (!_runActive) return;

            _runElapsedFixedTicks++;
            if (_runElapsedFixedTicks > _runMaxFixedTicks)
            {
                CompleteRun("MaxFixedTicks");
                return;
            }

            if (_runUntilSleeping)
            {
                if (IsWorldSleeping())
                {
                    CompleteRun("Sleeping");
                }
                return;
            }

            _runRemainingFixedTicks--;
            if (_runRemainingFixedTicks <= 0)
            {
                CompleteRun("FixedTicks");
            }
        }

        private bool IsWorldSleeping()
        {
            bool found = false;
            var chunks = _world.Query(in _runtimeStateQuery);
            foreach (var chunk in chunks)
            {
                var states = chunk.GetArray<Physics2DRuntimeState>();
                for (int i = 0; i < chunk.Count; i++)
                {
                    found = true;
                    if (states[i].AnyAwakeDynamicBodies) return false;
                }
            }

            return found;
        }

        private void CompleteRun(string reason)
        {
            ClearRunState();
            _tickPolicy.SetTargetHz(0);

            var ctx = _contextFactory();
            ctx.Set("Reason", reason);
            ctx.Set("FixedTicksElapsed", _runElapsedFixedTicks);
            _fireEvent(GameEvents.Physics2DRunCompleted, ctx);
        }

        private void ClearRunState()
        {
            _runActive = false;
            _runUntilSleeping = false;
            _runRemainingFixedTicks = 0;
            _runMaxFixedTicks = 0;
            _runElapsedFixedTicks = 0;
        }
    }
}
