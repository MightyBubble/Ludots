using Ludots.Core.Modding;

namespace CameraProfilesMod
{
    public sealed class CameraProfilesModEntry : IMod
    {
        public void OnLoad(IModContext context)
        {
            context.Log("[CameraProfilesMod] Loaded");
        }

        public void OnUnload()
        {
        }
    }
}
