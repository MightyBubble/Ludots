using Ludots.Core.Modding;

namespace RtsSc2TrainingShowcaseMod
{
    public sealed class RtsSc2TrainingShowcaseModEntry : IMod
    {
        public void OnLoad(IModContext context)
        {
            context.Log("[RtsSc2TrainingShowcaseMod] Loaded - SC2 training showcase");
        }

        public void OnUnload()
        {
        }
    }
}
