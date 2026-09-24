using Ludots.Core.Networking.Runtime;
using Ludots.Core.Scripting;

namespace Ludots.Adapter.LiteNetLib;

public static class LiteNetLibServiceKeys
{
    public static readonly ServiceKey<NetworkAcceptanceProofService> NetworkAcceptanceProof =
        new("LiteNetLib.NetworkAcceptanceProof");

    public static readonly ServiceKey<NetworkAdapterMetricsSampler> NetworkAdapterMetrics =
        new("LiteNetLib.NetworkAdapterMetrics");
}
