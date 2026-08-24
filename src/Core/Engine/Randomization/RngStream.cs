using System;

namespace Ludots.Core.Engine.Randomization
{
    public readonly record struct RngStreamSnapshot(uint State, long Position, string StreamId);

    /// <summary>
    /// A deterministic xorshift32 random stream. State only moves through the explicit
    /// Next/Advance calls, so identical seeds and call sequences reproduce identical results.
    /// </summary>
    public sealed class RngStream
    {
        internal const uint ZeroStateEscape = 2463534242u;

        private uint _state;

        internal RngStream(string streamId, uint seed)
        {
            StreamId = streamId;
            DeclaredSeed = seed;
            _state = seed == 0u ? ZeroStateEscape : seed;
        }

        public string StreamId { get; }

        public uint DeclaredSeed { get; }

        public long Position { get; private set; }

        public RngStreamSnapshot CaptureSnapshot() => new(_state, Position, StreamId);

        public void RestoreSnapshot(in RngStreamSnapshot snapshot)
        {
            if (snapshot.StreamId != StreamId)
            {
                throw new InvalidOperationException(
                    $"Snapshot belongs to stream '{snapshot.StreamId}' and cannot restore stream '{StreamId}'.");
            }

            _state = snapshot.State == 0u ? ZeroStateEscape : snapshot.State;
            Position = snapshot.Position;
        }

        public uint NextUInt()
        {
            var x = _state;
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            _state = x;
            Position++;
            return x;
        }

        public float NextFloat01() => (NextUInt() & 0x00FFFFFFu) / 16777215f;

        public int NextInt(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive)
            {
                throw new ArgumentOutOfRangeException(nameof(maxExclusive), "Upper bound must be greater than the lower bound.");
            }

            var range = (uint)((long)maxExclusive - minInclusive);
            var limit = uint.MaxValue - uint.MaxValue % range;
            uint sample;
            do
            {
                sample = NextUInt();
            } while (sample >= limit);

            return (int)(minInclusive + (long)(sample % range));
        }

        public void Advance(int steps)
        {
            if (steps < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(steps), "Cannot rewind a stream; restore a snapshot instead.");
            }

            for (var i = 0; i < steps; i++)
            {
                NextUInt();
            }
        }
    }
}
