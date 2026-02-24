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
            context.TriggerManager.RegisterTrigger(new TerrainBenchmarkOnEntryMapLoadedTrigger(context));
        }

        public void OnUnload()
        {
        }
    }
}

