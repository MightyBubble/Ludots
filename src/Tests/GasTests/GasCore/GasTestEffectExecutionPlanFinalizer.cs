using Ludots.Core.Gameplay.GAS;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;

namespace Ludots.Tests.GAS
{
    internal static class GasTestEffectExecutionPlanFinalizer
    {
        public static void FinalizeAll(
            EffectTemplateRegistry templates,
            PresetTypeRegistry presetTypes,
            BuiltinHandlerRegistry builtinHandlers,
            GraphProgramRegistry graphPrograms,
            string sourceName = "Test/effects.json")
        {
            EffectExecutionPlanCompiler.FinalizeAll(
                templates,
                presetTypes,
                builtinHandlers,
                graphPrograms,
                GasGraphOpHandlerTable.Instance,
                sourceName);
        }
    }
}
