using Ludots.Core.Scripting;
using MassNavWebParityMod.Runtime;

namespace MassNavWebParityMod;

internal static class MassNavWebParityKeys
{
    public static readonly ServiceKey<MassNavSimulationRuntime> SimulationRuntime = new("MassNavWebParity_SimulationRuntime");
}
