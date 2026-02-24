using Ludots.Core.Modding;

namespace VersionMismatchMod
{
    public class VersionMismatchModEntry : IMod
    {
        public void OnLoad(IModContext context)
        {
            context.Log("VersionMismatchMod Loaded!");
        }

        public void OnUnload()
        {
        }
    }
}
