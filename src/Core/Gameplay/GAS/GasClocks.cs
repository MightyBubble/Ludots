using System;
using Ludots.Core.Engine;

namespace Ludots.Core.Gameplay.GAS
{
    public sealed class GasClocks
    {
        private readonly IClock _clock;

        public GasClocks(IClock clock)
        {
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        }

        public int FixedFrameNow => _clock.Now(ClockDomainId.FixedFrame);
        public int StepNow => _clock.Now(ClockDomainId.Step);
        public int TurnNow => _clock.Now(ClockDomainId.Turn);

        public int Now(GasClockId clockId)
        {
            return _clock.Now(clockId.ToDomainId());
        }

        public void AdvanceFixedFrame()
        {
            _clock.Advance(ClockDomainId.FixedFrame);
        }

        public void AdvanceStep()
        {
            _clock.Advance(ClockDomainId.Step);
        }

        public void AdvanceTurn()
        {
            _clock.Advance(ClockDomainId.Turn);
        }

        public void Reset()
        {
            throw new InvalidOperationException("GasClocks no longer owns time state. Reset must be handled by the clock authority.");
        }
    }
}
