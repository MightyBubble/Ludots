using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using TerrainBenchmarkMod.Triggers;

namespace TerrainBenchmarkMod
{
    public sealed class TerrainBenchmarkModEntry : IMod
    {
        public void OnLoad(IModContext context)
        {
            context.Log("[TerrainBenchmarkMod] Loaded");
            TerrainBenchmarkMapGenerator.EnsureGenerated(context);
            context.OnEvent(GameEvents.MapLoaded, new TerrainBenchmarkOnEntryMapLoadedTrigger(context).ExecuteAsync);
        }

        public void OnUnload()
        {
        }
    }
}

