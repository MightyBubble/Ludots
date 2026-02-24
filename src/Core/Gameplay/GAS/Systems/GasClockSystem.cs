using Arch.System;
using Ludots.Core.Engine;

namespace Ludots.Core.Gameplay.GAS.Systems
{
    public sealed class GasClockSystem : ISystem<float>
    {
        private readonly IClock _clock;
        private readonly GasClockStepPolicy _stepPolicy;

        public GasClockSystem(IClock clock, GasClockStepPolicy stepPolicy)
        {
            _clock = clock;
            _stepPolicy = stepPolicy;
        }

        public void Initialize() { }

        public void Update(in float dt)
        {
            _clock.Advance(ClockDomainId.FixedFrame);
            if (_stepPolicy.ShouldAdvanceStepOnThisFixedTick())
            {
                _clock.Advance(ClockDomainId.Step);
            }
        }

        public void BeforeUpdate(in float dt) { }
        public void AfterUpdate(in float dt) { }
        public void Dispose() { }
    }
}
