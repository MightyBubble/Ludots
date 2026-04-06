using Ludots.Core.Scripting;
using MinimapControlMod.Runtime;

namespace MinimapControlMod;

public static class MinimapControlServiceKeys
{
    public static readonly ServiceKey<MinimapControlRuntime> Runtime =
        new("MinimapControlMod.Runtime");
}
