using System;
using Ludots.Core.Networking.Session;

namespace Ludots.Core.Networking.Runtime
{
    /// <summary>
    /// Identity of one client command sequence stream. A reconnect to the same identity continues
    /// its cursor; any epoch, seat slot, or seat generation change starts a distinct stream.
    /// </summary>
    public readonly struct ReplicatedClientCommandStreamIdentity : IEquatable<ReplicatedClientCommandStreamIdentity>
    {
        public const ulong FirstBatchSequence = 1;

        public ReplicatedClientCommandStreamIdentity(
            SessionEpoch sessionEpoch,
            int seatSlot,
            uint seatGeneration)
        {
            if (sessionEpoch.IsEmpty)
            {
                throw new ArgumentException("Command stream session epoch must be non-empty.", nameof(sessionEpoch));
            }

            if (seatSlot < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(seatSlot));
            }

            if (seatGeneration == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(seatGeneration));
            }

            SessionEpoch = sessionEpoch;
            SeatSlot = seatSlot;
            SeatGeneration = seatGeneration;
        }

        public SessionEpoch SessionEpoch { get; }

        public int SeatSlot { get; }

        public uint SeatGeneration { get; }

        public bool IsValid => !SessionEpoch.IsEmpty && SeatSlot >= 0 && SeatGeneration != 0;

        public bool Equals(ReplicatedClientCommandStreamIdentity other) =>
            SessionEpoch == other.SessionEpoch &&
            SeatSlot == other.SeatSlot &&
            SeatGeneration == other.SeatGeneration;

        public override bool Equals(object? obj) =>
            obj is ReplicatedClientCommandStreamIdentity other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(SessionEpoch, SeatSlot, SeatGeneration);

        public static bool operator ==(
            ReplicatedClientCommandStreamIdentity left,
            ReplicatedClientCommandStreamIdentity right) => left.Equals(right);

        public static bool operator !=(
            ReplicatedClientCommandStreamIdentity left,
            ReplicatedClientCommandStreamIdentity right) => !left.Equals(right);
    }

    public enum NetworkProcessRole : byte
    {
        Standalone = 0,
        AuthoritativeServer = 1,
        ReplicatedClient = 2,
    }

    /// <summary>
    /// Platform-neutral lifecycle driven by the engine. Transport adapters implement this port;
    /// Core never owns sockets, windows, or host process concerns.
    /// </summary>
    public interface INetworkRuntimePort : IDisposable
    {
        NetworkProcessRole Role { get; }

        /// <summary>
        /// Activates the runtime after every GameStart handler has registered its network schemas.
        /// The engine completes this lifecycle step before publishing NetworkRuntimeReady.
        /// </summary>
        void Activate();

        void PumpTransport();

        void BeforeAuthoritativeTick(uint executingTick);

        void AfterAuthoritativeCommit(uint committedTick);

        void PumpReplicatedClient(float frameDeltaTime);
    }
}
