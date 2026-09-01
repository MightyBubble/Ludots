using System;

namespace Ludots.Core.Networking.Replication
{
    public readonly struct NetworkEntityHandle : IEquatable<NetworkEntityHandle>
    {
        public NetworkEntityHandle(int slot, uint generation)
        {
            if (slot < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(slot));
            }

            if (generation == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(generation));
            }

            Slot = slot;
            Generation = generation;
        }

        public int Slot { get; }

        public uint Generation { get; }

        public bool IsValid => Generation != 0;

        /// <summary>
        /// Creates a handle from wire or mirror data without throwing on malformed input.
        /// </summary>
        public static bool TryCreate(int slot, uint generation, out NetworkEntityHandle handle)
        {
            if (slot < 0 || generation == 0)
            {
                handle = default;
                return false;
            }

            handle = new NetworkEntityHandle(slot, generation);
            return true;
        }

        public bool Equals(NetworkEntityHandle other)
            => Slot == other.Slot && Generation == other.Generation;

        public override bool Equals(object? obj)
            => obj is NetworkEntityHandle other && Equals(other);

        public override int GetHashCode()
            => HashCode.Combine(Slot, Generation);

        public static bool operator ==(NetworkEntityHandle left, NetworkEntityHandle right)
            => left.Equals(right);

        public static bool operator !=(NetworkEntityHandle left, NetworkEntityHandle right)
            => !left.Equals(right);
    }
}
