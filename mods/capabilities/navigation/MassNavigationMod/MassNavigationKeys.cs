using Ludots.Core.Scripting;
using MassNavigationMod.Runtime;

namespace MassNavigationMod;

public static class MassNavigationKeys
{
    public static readonly ServiceKey<MassNavigationSimulationRuntime> SimulationRuntime = new("MassNavigation_SimulationRuntime");
    public static readonly ServiceKey<MassNavigationShowcaseGuideRuntime> ShowcaseGuideRuntime = new("MassNavigation_ShowcaseGuideRuntime");
}

