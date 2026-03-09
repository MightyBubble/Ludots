using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using VirtualCameraShotsMod.Triggers;

namespace VirtualCameraShotsMod
{
    public sealed class VirtualCameraShotsModEntry : IMod
    {
        public void OnLoad(IModContext context)
        {
            context.Log("[VirtualCameraShotsMod] Loaded");
            context.OnEvent(GameEvents.MapLoaded, new ActivateTaggedVirtualCameraOnMapLoadedTrigger(context).ExecuteAsync);
        }

        public void OnUnload()
        {
        }
    }
}
