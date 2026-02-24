using System;
using Arch.Core;
using Ludots.Core.Engine.Pacemaker;
using Ludots.Core.Scripting;

namespace Ludots.Core.Gameplay.GAS
{
    public sealed class GasController
    {
        private readonly World _world;
        private readonly GasClockStepPolicy _stepPolicy;
        private readonly SimulationLoopController _loop;
        private readonly Func<ScriptContext> _contextFactory;
        private readonly Action<EventKey, ScriptContext> _fireEvent;

        private int _runMaxFixedTicks;
        private int _runElapsedFixedTicks;
        private bool _runActive;

        private static readonly QueryDescription _runtimeQuery = new QueryDescription().WithAll<GasRuntimeState>();

        public GasController(World world, GasClockStepPolicy stepPolicy, SimulationLoopController loop, Func<ScriptContext> contextFactory, Action<EventKey, ScriptContext> fireEvent)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _stepPolicy = stepPolicy ?? throw new ArgumentNullException(nameof(stepPolicy));
            _loop = loop ?? throw new ArgumentNullException(nameof(loop));
            _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
            _fireEvent = fireEvent ?? throw new ArgumentNullException(nameof(fireEvent));
        }

        public bool IsRunning => _runActive;

        public void RunUntilEffectWindowsClosed(int maxFixedTicks = 10_000)
        {
            if (maxFixedTicks < 1) throw new ArgumentOutOfRangeException(nameof(maxFixedTicks));

            _runActive = true;
            _runMaxFixedTicks = maxFixedTicks;
            _runElapsedFixedTicks = 0;

            var ctx = _contextFactory();
            ctx.Set("Mode", "RunUntilEffectWindowsClosed");
            ctx.Set("MaxFixedTicks", maxFixedTicks);
            ctx.Set("GasStepMode", _stepPolicy.Mode.ToString());
            _fireEvent(GameEvents.GasRunStarted, ctx);

            _loop.SetTurnBased();
            _loop.Step(1);
        }

        public void Cancel()
        {
            if (!_runActive) return;
            _runActive = false;
            Complete("Canceled", default);
        }

        public void AfterFixedTick()
        {
            if (!_runActive) return;

            _runElapsedFixedTicks++;
            if (_runElapsedFixedTicks > _runMaxFixedTicks)
            {
                Complete("MaxFixedTicks", ReadRuntimeState());
                _runActive = false;
                return;
            }

            var s = ReadRuntimeState();
            if (!s.HasValue)
            {
                Complete("MissingRuntimeState", default);
                _runActive = false;
                return;
            }

            var state = s.Value;
            if (IsIdle(state))
            {
                Complete("Idle", state);
                _runActive = false;
                return;
            }

            if (state.ProposalWaitingInput && state.ChainOrderCount == 0)
            {
                Complete("BlockedOnInput", state);
                _runActive = false;
                return;
            }

            _loop.Step(1);
        }

        private bool IsIdle(in GasRuntimeState state)
        {
            if (state.ProposalWindowPhase != 0) return false;
            if (state.EffectRequestCount != 0) return false;
            if (state.HasPendingEffects) return false;
            if (state.InputRequestCount != 0) return false;
            if (state.ChainOrderCount != 0) return false;
            if (state.OrderRequestCount != 0) return false;
            return true;
        }

        private GasRuntimeState? ReadRuntimeState()
        {
            var chunks = _world.Query(in _runtimeQuery);
            foreach (var chunk in chunks)
            {
                var states = chunk.GetArray<GasRuntimeState>();
                for (int i = 0; i < chunk.Count; i++)
                {
                    return states[i];
                }
            }

            return null;
        }

        private void Complete(string reason, GasRuntimeState? state)
        {
            var ctx = _contextFactory();
            ctx.Set("Reason", reason);
            ctx.Set("FixedTicksElapsed", _runElapsedFixedTicks);
            if (state.HasValue)
            {
                ctx.Set("ProposalWindowPhase", state.Value.ProposalWindowPhase);
                ctx.Set("HasPendingEffects", state.Value.HasPendingEffects);
                ctx.Set("EffectRequestCount", state.Value.EffectRequestCount);
                ctx.Set("InputRequestCount", state.Value.InputRequestCount);
                ctx.Set("ChainOrderCount", state.Value.ChainOrderCount);
                ctx.Set("OrderRequestCount", state.Value.OrderRequestCount);
            }
            _fireEvent(GameEvents.GasRunCompleted, ctx);
        }
    }
}

