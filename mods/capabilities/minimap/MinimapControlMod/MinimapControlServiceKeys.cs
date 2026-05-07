using Ludots.Core.Scripting;
using MinimapControlMod.Runtime;

namespace MinimapControlMod;

public static class MinimapControlServiceKeys
{
    public static readonly ServiceKey<MinimapControlRuntime> Runtime =
        new("MinimapControlMod.Runtime");

    public static readonly ServiceKey<MinimapWorldClickRequest> WorldClickRequest =
        new("MinimapControlMod.WorldClickRequest");
}
