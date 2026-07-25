using Ludots.Core.Scripting;
using Ludots.Core.Physics3DNet.Client;

namespace Ludots.Core.Physics3DNet.Bridge;

public static class Physics3DNetworkServiceKeys
{
    public static readonly ServiceKey<Physics3DNetworkBodyRegistry> BodyRegistry =
        new("Physics3D.NetworkBodyRegistry");

    public static readonly ServiceKey<IPhysics3DClientInputSource> ClientInputSource =
        new("Physics3D.ClientInputSource");

    public static readonly ServiceKey<IPhysics3DLocalPredictionDriver> LocalPredictionDriver =
        new("Physics3D.LocalPredictionDriver");

    public static readonly ServiceKey<Physics3DReplicatedClientConvergence> ClientConvergence =
        new("Physics3D.ClientConvergence");
}
