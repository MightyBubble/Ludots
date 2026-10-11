using Ludots.Core.Modding;

namespace MobaDemoMod
{
    public sealed class MobaDemoModEntry : IMod
    {
        public void OnLoad(IModContext context)
        {
            context.Log("[MobaDemoMod] Loaded");
        }

        public void OnUnload()
        {
        }
    }
}
