using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using StrategicDomainMod.Triggers;

namespace StrategicDomainMod
{
    public sealed class StrategicDomainModEntry : IMod
    {
        public void OnLoad(IModContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            context.Log("[StrategicDomainMod] Loaded.");
            context.OnEvent(
                GameEvents.GameStart,
                new InstallStrategicDomainOnGameStartTrigger(context).ExecuteAsync);
        }

        public void OnUnload()
        {
        }
    }
}
