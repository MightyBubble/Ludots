using Ludots.Core.Engine;

namespace Ludots.Core.Gameplay.GAS
{
    public static class GasClockDomainIdExtensions
    {
        public static ClockDomainId ToDomainId(this GasClockId id) => (ClockDomainId)(byte)id;
    }
}
