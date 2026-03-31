using Ludots.Core.Modding;

namespace CommonControlBuffsMod
{
    public sealed class CommonControlBuffsModEntry : IMod
    {
        public void OnLoad(IModContext context)
        {
            context.Log("[CommonControlBuffsMod] Loaded");
        }

        public void OnUnload()
        {
        }
    }
}
