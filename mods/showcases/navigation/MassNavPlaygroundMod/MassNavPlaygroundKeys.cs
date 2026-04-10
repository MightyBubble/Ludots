using Ludots.Core.Scripting;
using MassNavPlaygroundMod.Runtime;

namespace MassNavPlaygroundMod;

internal static class MassNavPlaygroundKeys
{
    public static readonly ServiceKey<MassNavSimulationRuntime> SimulationRuntime = new("MassNavPlayground_SimulationRuntime");
}
