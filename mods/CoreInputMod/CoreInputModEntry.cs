using Ludots.Core.Modding;

namespace CoreInputMod
{
    public sealed class CoreInputModEntry : IMod
    {
        public void OnLoad(IModContext context)
        {
            context.Log("[CoreInputMod] Loaded - generic command-source and GAS input");
        }

        public void OnUnload() { }
    }
}
