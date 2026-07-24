using System;

namespace Ludots.Core.Networking.Runtime
{
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

        void PumpTransport();

        void BeforeAuthoritativeTick(uint executingTick);

        void AfterAuthoritativeCommit(uint committedTick);

        void PumpReplicatedClient(float frameDeltaTime);
    }
}
