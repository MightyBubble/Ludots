namespace Ludots.Core.Engine
{
    public sealed record DiscreteClockDomainSnapshot(ClockDomainId Domain, int Tick);

    public sealed record DiscreteClockSnapshot(IReadOnlyList<DiscreteClockDomainSnapshot> Domains);

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

        public DiscreteClockSnapshot CaptureSnapshot()
        {
            ClockDomainId[] domains = System.Enum.GetValues<ClockDomainId>();
            var entries = new DiscreteClockDomainSnapshot[domains.Length];
            for (int i = 0; i < domains.Length; i++)
            {
                ClockDomainId domain = domains[i];
                entries[i] = new DiscreteClockDomainSnapshot(domain, Now(domain));
            }

            return new DiscreteClockSnapshot(entries);
        }

        public void RestoreSnapshot(DiscreteClockSnapshot snapshot)
        {
            if (snapshot == null) throw new System.ArgumentNullException(nameof(snapshot));

            System.Array.Clear(_ticks, 0, _ticks.Length);
            var seen = new bool[_ticks.Length];
            for (int i = 0; i < snapshot.Domains.Count; i++)
            {
                DiscreteClockDomainSnapshot domain = snapshot.Domains[i];
                int index = (int)domain.Domain;
                if ((uint)index >= (uint)_ticks.Length)
                {
                    throw new System.ArgumentOutOfRangeException(nameof(snapshot), "Clock snapshot contains an unknown domain.");
                }

                if (seen[index])
                {
                    throw new System.ArgumentException("Clock snapshot contains a duplicate domain.", nameof(snapshot));
                }

                if (domain.Tick < 0)
                {
                    throw new System.ArgumentOutOfRangeException(nameof(snapshot), "Clock snapshot tick must be non-negative.");
                }

                seen[index] = true;
                _ticks[index] = domain.Tick;
            }
        }
    }
}
