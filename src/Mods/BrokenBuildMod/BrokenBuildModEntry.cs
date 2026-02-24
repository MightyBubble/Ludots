using Ludots.Core.Modding;

namespace BrokenBuildMod
{
    public class BrokenBuildModEntry : IMod
    {
        public void OnLoad(IModContext context)
        {
            var x = DoesNotCompile;
            context.Log($"BrokenBuildMod Loaded: {x}");
        }

        public void OnUnload()
        {
        }
    }
}
