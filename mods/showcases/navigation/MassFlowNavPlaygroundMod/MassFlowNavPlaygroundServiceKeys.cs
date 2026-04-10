using Ludots.Core.Scripting;
using MassFlowNavPlaygroundMod.Runtime;

namespace MassFlowNavPlaygroundMod
{
    internal static class MassFlowNavPlaygroundServiceKeys
    {
        public static readonly ServiceKey<bool> Installed = new("MassFlowNavPlaygroundMod.Installed");
        public static readonly ServiceKey<MassFlowNavPlaygroundState> State = new("MassFlowNavPlaygroundMod.State");
    }
}
