using Ludots.Core.Modding;

namespace TerrainNavClosedLoopShowcaseMod
{
    public sealed class TerrainNavClosedLoopShowcaseModEntry : IMod
    {
        public void OnLoad(IModContext context)
        {
            context.Log("[TerrainNavClosedLoopShowcaseMod] Loaded");
        }

        public void OnUnload()
        {
        }
    }
}
