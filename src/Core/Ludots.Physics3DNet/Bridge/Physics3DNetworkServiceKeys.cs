using Ludots.Core.Scripting;

namespace Ludots.Core.Physics3DNet.Bridge;

public static class Physics3DNetworkServiceKeys
{
    public static readonly ServiceKey<Physics3DNetworkBodyRegistry> BodyRegistry =
        new("Physics3D.NetworkBodyRegistry");
}
