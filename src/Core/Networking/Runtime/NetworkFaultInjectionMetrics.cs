using System;

namespace Ludots.Core.Networking.Runtime
{
    public readonly struct NetworkFaultInjectionConfigurationSnapshot
    {
        public NetworkFaultInjectionConfigurationSnapshot(
            string transportIdentity,
            string profileId,
            int seed,
            int roundTripLatencyMilliseconds,
            int jitterMilliseconds,
            int packetLossPermille,
            int stateReorderPermille)
        {
            if (string.IsNullOrWhiteSpace(transportIdentity))
            {
                throw new ArgumentException("Transport identity is required.", nameof(transportIdentity));
            }
            if (string.IsNullOrWhiteSpace(profileId))
            {
                throw new ArgumentException("Fault profile id is required.", nameof(profileId));
            }
            if (seed <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(seed));
            }
            if (roundTripLatencyMilliseconds < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(roundTripLatencyMilliseconds));
            }
            if (jitterMilliseconds < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(jitterMilliseconds));
            }
            if ((uint)packetLossPermille > 1000u)
            {
                throw new ArgumentOutOfRangeException(nameof(packetLossPermille));
            }
            if ((uint)stateReorderPermille > 1000u)
            {
                throw new ArgumentOutOfRangeException(nameof(stateReorderPermille));
            }

            TransportIdentity = transportIdentity;
            ProfileId = profileId;
            Seed = seed;
            RoundTripLatencyMilliseconds = roundTripLatencyMilliseconds;
            JitterMilliseconds = jitterMilliseconds;
            PacketLossPermille = packetLossPermille;
            StateReorderPermille = stateReorderPermille;
        }

        public string TransportIdentity { get; }
        public string ProfileId { get; }
        public int Seed { get; }
        public int RoundTripLatencyMilliseconds { get; }
        public int JitterMilliseconds { get; }
        public int PacketLossPermille { get; }
        public int StateReorderPermille { get; }

        public bool IsValid =>
            !string.IsNullOrWhiteSpace(TransportIdentity) &&
            !string.IsNullOrWhiteSpace(ProfileId) &&
            Seed > 0;

        public bool IsEnabled =>
            RoundTripLatencyMilliseconds != 0 ||
            JitterMilliseconds != 0 ||
            PacketLossPermille != 0 ||
            StateReorderPermille != 0;
    }

    public readonly struct NetworkFaultInjectionObservationSnapshot
    {
        public NetworkFaultInjectionObservationSnapshot(
            NetworkProcessRole role,
            in NetworkFaultInjectionConfigurationSnapshot configuration,
            long delayedInboundPacketCount,
            long droppedInboundPacketCount,
            long reorderedInboundStateDatagramCount)
        {
            if (role == NetworkProcessRole.Standalone)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(role),
                    "Fault injection observations require an active network process role.");
            }
            if (!configuration.IsValid)
            {
                throw new ArgumentException("Fault injection configuration snapshot is invalid.", nameof(configuration));
            }
            if (delayedInboundPacketCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(delayedInboundPacketCount));
            }
            if (droppedInboundPacketCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(droppedInboundPacketCount));
            }
            if (reorderedInboundStateDatagramCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(reorderedInboundStateDatagramCount));
            }

            Role = role;
            Configuration = configuration;
            DelayedInboundPacketCount = delayedInboundPacketCount;
            DroppedInboundPacketCount = droppedInboundPacketCount;
            ReorderedInboundStateDatagramCount = reorderedInboundStateDatagramCount;
        }

        public NetworkProcessRole Role { get; }
        public NetworkFaultInjectionConfigurationSnapshot Configuration { get; }
        public long DelayedInboundPacketCount { get; }
        public long DroppedInboundPacketCount { get; }
        public long ReorderedInboundStateDatagramCount { get; }
    }

    /// <summary>
    /// Platform-neutral, allocation-free read boundary for effective transport fault settings and
    /// events that were actually injected into this process's inbound traffic.
    /// </summary>
    public interface INetworkFaultInjectionMetricsPort
    {
        NetworkFaultInjectionObservationSnapshot Capture();
    }
}
