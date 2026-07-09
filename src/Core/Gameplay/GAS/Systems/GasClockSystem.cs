using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Scripting;

namespace Ludots.Core.Gameplay.GAS.Systems
{
    public sealed class GasClockSystem : ISystem<float>
    {
        private readonly IClock _clock;
        private readonly GasClockStepPolicy _stepPolicy;
        private readonly System.Func<ScriptContext>? _contextFactory;
        private readonly System.Action<EventKey, ScriptContext>? _fireEvent;

        public GasClockSystem(
            IClock clock,
            GasClockStepPolicy stepPolicy,
            System.Func<ScriptContext>? contextFactory = null,
            System.Action<EventKey, ScriptContext>? fireEvent = null)
        {
            _clock = clock;
            _stepPolicy = stepPolicy;
            _contextFactory = contextFactory;
            _fireEvent = fireEvent;
        }

        public void Initialize() { }

        public void Update(in float dt)
        {
            _clock.Advance(ClockDomainId.FixedFrame);
            int steps = _stepPolicy.ConsumeStepsForThisFixedTick();
            for (int i = 0; i < steps; i++)
            {
                _clock.Advance(ClockDomainId.Step);
                if (i < _stepPolicy.LastConsumedManualSteps)
                {
                    FireTurnAdvanced();
                }
            }
        }

        private void FireTurnAdvanced()
        {
            if (_contextFactory == null || _fireEvent == null) return;

            ScriptContext ctx = _contextFactory();
            ctx.Set("Step", _clock.Now(ClockDomainId.Step));
            _fireEvent(GameEvents.TurnAdvanced, ctx);
        }

        public void BeforeUpdate(in float dt) { }
        public void AfterUpdate(in float dt) { }
        public void Dispose() { }
    }
}
