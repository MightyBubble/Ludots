using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using CameraBootstrapMod.Triggers;

namespace CameraBootstrapMod
{
    public sealed class CameraBootstrapModEntry : IMod
    {
        public void OnLoad(IModContext context)
        {
            context.Log("[CameraBootstrapMod] Loaded");
            context.OnEvent(GameEvents.MapLoaded, new CameraBootstrapOnMapLoadedTrigger(context).ExecuteAsync);
        }

        public void OnUnload()
        {
        }
    }
}
