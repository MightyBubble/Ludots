using Ludots.Core.Scripting;
using StrategicDomainMod.Runtime;

namespace StrategicDomainMod
{
    public static class StrategicDomainServiceKeys
    {
        public static readonly ServiceKey<StrategicDomainRuntime> Runtime = new("StrategicDomainRuntime");
    }
}
