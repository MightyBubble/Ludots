namespace Ludots.Core.Engine
{
    public enum ClockDomainId : byte
    {
        FixedFrame = 0,
        Step = 1,
        Turn = 2,
        PhysicsStep = 3,
        NavigationStep = 4
    }

    public interface IClock
    {
        int Now(ClockDomainId domain);
        void Advance(ClockDomainId domain, int ticks = 1);
    }

    public sealed class DiscreteClock : IClock
    {
        private readonly int[] _ticks = new int[8];

        public int Now(ClockDomainId domain)
        {
            int index = (int)domain;
            if ((uint)index >= (uint)_ticks.Length) throw new System.ArgumentOutOfRangeException(nameof(domain));
            return _ticks[index];
        }

        public void Advance(ClockDomainId domain, int ticks = 1)
        {
            if (ticks == 0) return;
            if (ticks < 0) throw new System.ArgumentOutOfRangeException(nameof(ticks));
            int index = (int)domain;
            if ((uint)index >= (uint)_ticks.Length) throw new System.ArgumentOutOfRangeException(nameof(domain));
            _ticks[index] += ticks;
        }
    }
}
