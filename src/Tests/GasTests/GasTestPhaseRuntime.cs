using Arch.Core;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Systems;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;

namespace Ludots.Tests.GAS;

internal static class GasTestPhaseRuntime
{
    public static void Create(
        World world,
        EffectTemplateRegistry templates,
        EffectRequestQueue? effectRequests,
        out EffectPhaseExecutor phaseExecutor,
        out GasGraphRuntimeApi graphApi,
        GraphProgramRegistry? graphPrograms = null,
        PresetTypeRegistry? presetTypes = null,
        BuiltinHandlerRegistry? builtinHandlers = null,
        TagOps? tagOps = null)
    {
        graphPrograms ??= new GraphProgramRegistry();
        presetTypes ??= new PresetTypeRegistry();
        if (builtinHandlers == null)
        {
            builtinHandlers = new BuiltinHandlerRegistry();
            BuiltinHandlers.RegisterAll(builtinHandlers);
        }

        phaseExecutor = new EffectPhaseExecutor(
            graphPrograms,
            presetTypes,
            builtinHandlers,
            GasGraphOpHandlerTable.Instance,
            templates);
        graphApi = new GasGraphRuntimeApi(
            world,
            spatialQueries: null,
            coords: null,
            eventBus: null,
            effectRequests: effectRequests,
            tagOps: tagOps);
    }

    public static int EnsureBuiltinGraph(
        GraphProgramRegistry programs,
        string graphName,
        BuiltinHandlerId handlerId)
    {
        int graphId = GraphIdRegistry.GetId(graphName);
        if (graphId <= 0)
        {
            graphId = GraphIdRegistry.Register(graphName);
        }

        GasTestGraphPrograms.RegisterBuiltinGraph(programs, graphId, handlerId);
        return graphId;
    }
}
