using Ludots.Core.Modding;

namespace CycleBMod
{
    public class CycleBModEntry : IMod
    {
        public void OnLoad(IModContext context)
        {
            context.Log("CycleBMod Loaded!");
        }

        public void OnUnload()
        {
        }
    }
}
