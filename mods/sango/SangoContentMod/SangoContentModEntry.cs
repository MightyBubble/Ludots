using Ludots.Core.Modding;

namespace Sango.Content
{
    public sealed class SangoContentModEntry : IMod
    {
        public void OnLoad(IModContext context)
        {
            context.Log("[SangoContentMod] Loaded (assets/Data tables + assets/Scenario; table loading lands with the sim milestone).");
        }

        public void OnUnload()
        {
        }
    }
}
