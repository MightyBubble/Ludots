using Ludots.Core.Modding;

namespace RtsShowcaseMod
{
    public class RtsShowcaseModEntry : IMod
    {
        public void OnLoad(IModContext context)
        {
            context.Log("[RtsShowcaseMod] Loaded — RTS Showcase with SC2/RA2/War3 maps");
        }

        public void OnUnload() { }
    }
}
