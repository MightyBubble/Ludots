using Ludots.Core.Modding;

namespace AIDemoMod
{
    public class AIDemoModEntry : IMod
    {
        public void OnLoad(IModContext context)
        {
            context.Log("[AIDemoMod] Loaded.");
        }

        public void OnUnload()
        {
        }
    }
}

