using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using EntityQueryTacticsShowcaseMod.Runtime;
using EntityQueryTacticsShowcaseMod.Triggers;

namespace EntityQueryTacticsShowcaseMod
{
    public sealed class EntityQueryTacticsShowcaseModEntry : IMod
    {
        public void OnLoad(IModContext context)
        {
            context.OnEvent(GameEvents.GameStart, new InstallEntityQueryTacticsShowcaseOnGameStartTrigger(context).ExecuteAsync);
        }

        public void OnUnload()
        {
        }

    }
}
