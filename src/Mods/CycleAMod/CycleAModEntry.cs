using Ludots.Core.Modding;

namespace CycleAMod
{
    public class CycleAModEntry : IMod
    {
        public void OnLoad(IModContext context)
        {
            context.Log("CycleAMod Loaded!");
        }

        public void OnUnload()
        {
        }
    }
}
