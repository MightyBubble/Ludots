using Ludots.Core.Modding;

namespace MissingDepMod
{
    public class MissingDepModEntry : IMod
    {
        public void OnLoad(IModContext context)
        {
            context.Log("MissingDepMod Loaded!");
        }

        public void OnUnload()
        {
        }
    }
}
