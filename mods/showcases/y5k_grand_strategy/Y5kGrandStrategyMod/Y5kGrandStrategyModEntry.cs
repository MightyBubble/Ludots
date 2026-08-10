using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using Y5kGrandStrategyMod.Triggers;

namespace Y5kGrandStrategyMod;

public sealed class Y5kGrandStrategyModEntry : IMod
{
	public void OnLoad(IModContext context)
	{
		ArgumentNullException.ThrowIfNull(context);
		context.Log("[Y5kGrandStrategyMod] Loaded — content + HUD assembly only.");
		context.OnEvent(GameEvents.GameStart, new InstallY5kWorldOnGameStartTrigger(context).ExecuteAsync);
		context.OnEvent(GameEvents.MapLoaded, new InstallY5kWorldOnGameStartTrigger(context).HandleMapLoadedAsync);
	}

	public void OnUnload()
	{
	}
}
