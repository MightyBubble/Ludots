using Ludots.Core.Modding;

namespace RtsWar3TrainingShowcaseMod
{
    public sealed class RtsWar3TrainingShowcaseModEntry : IMod
    {
        public void OnLoad(IModContext context)
        {
            context.Log("[RtsWar3TrainingShowcaseMod] Loaded - Warcraft training showcase");
        }

        public void OnUnload()
        {
        }
    }
}
