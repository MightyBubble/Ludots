using Ludots.Core.Diagnostics;

namespace Ludots.AgentBridge
{
    /// <summary>
    /// Bounded in-memory log ring backing ludots.logs.tail. Installed via
    /// Log.AddBackend so the host's configured backend and levels are untouched.
    /// </summary>
    public sealed class AgentLogRingBackend : ILogBackend
    {
        public readonly struct Entry
        {
            public Entry(DateTime utc, LogLevel level, string channel, string message)
            {
                Utc = utc;
                Level = level;
                Channel = channel;
                Message = message;
            }

            public DateTime Utc { get; }
            public LogLevel Level { get; }
            public string Channel { get; }
            public string Message { get; }
        }

        private readonly object _gate = new();
        private readonly Entry[] _ring;
        private int _next;

        public AgentLogRingBackend(int capacity = 2048)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            _ring = new Entry[capacity];
        }

        public int Capacity => _ring.Length;
        public int Count { get; private set; }
        public long TotalWritten { get; private set; }

        public void Write(LogLevel level, in LogChannel channel, string message)
        {
            lock (_gate)
            {
                _ring[_next] = new Entry(DateTime.UtcNow, level, channel.Name, message);
                _next = (_next + 1) % _ring.Length;
                if (Count < _ring.Length) Count++;
                TotalWritten++;
            }
        }

        /// <summary>Most recent entries in chronological order.</summary>
        public List<Entry> Snapshot(int count)
        {
            lock (_gate)
            {
                int take = Math.Min(count, Count);
                var result = new List<Entry>(take);
                int start = (_next - take + _ring.Length) % _ring.Length;
                for (int i = 0; i < take; i++)
                {
                    result.Add(_ring[(start + i) % _ring.Length]);
                }

                return result;
            }
        }

        public void Flush() { }
        public void Dispose() { }
    }
}
