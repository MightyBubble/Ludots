using Ludots.Core.Modding;

namespace RtsCncTrainingShowcaseMod
{
    public sealed class RtsCncTrainingShowcaseModEntry : IMod
    {
        public void OnLoad(IModContext context)
        {
            context.Log("[RtsCncTrainingShowcaseMod] Loaded - C&C training showcase");
        }

        public void OnUnload()
        {
        }
    }
}
