using Ludots.Core.Scripting;
using NarrativeFrontendMod.Runtime;

namespace NarrativeFrontendMod;

public static class NarrativeFrontendServiceKeys
{
    public static readonly ServiceKey<NarrativeFrontendService> Service =
        new("NarrativeFrontendMod.Service");
}
