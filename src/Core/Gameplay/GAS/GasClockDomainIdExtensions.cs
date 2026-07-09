using Ludots.Core.Engine;

namespace Ludots.Core.Gameplay.GAS
{
    public static class GasClockDomainIdExtensions
    {
        public static ClockDomainId ToDomainId(this GasClockId id) => id switch
        {
            GasClockId.FixedFrame => ClockDomainId.FixedFrame,
            GasClockId.Step => ClockDomainId.Step,
            _ => throw new System.InvalidOperationException($"GasClockId '{id}' is not a global clock domain.")
        };
    }
}
