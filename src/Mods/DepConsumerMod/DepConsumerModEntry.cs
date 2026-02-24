using DepApiMod;
using Ludots.Core.Modding;

namespace DepConsumerMod
{
    public class DepConsumerModEntry : IMod
    {
        public void OnLoad(IModContext context)
        {
            var marker = new DepApiMarker();
            context.Log($"DepConsumerMod Loaded! DepApiMod ping: {marker.Ping()}");
        }

        public void OnUnload()
        {
        }
    }
}
