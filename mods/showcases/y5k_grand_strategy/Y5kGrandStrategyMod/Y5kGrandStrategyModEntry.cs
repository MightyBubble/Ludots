using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using Y5kGrandStrategyMod.Triggers;

namespace Y5kGrandStrategyMod;

public sealed class Y5kGrandStrategyModEntry : IMod
{
	private InstallY5kHudOnGameStartTrigger? _hudTrigger;

	public void OnLoad(IModContext context)
	{
		ArgumentNullException.ThrowIfNull(context);
		context.Log("[Y5kGrandStrategyMod] Loaded — content + HUD assembly only.");
		var worldTrigger = new InstallY5kWorldOnGameStartTrigger(context);
		_hudTrigger = new InstallY5kHudOnGameStartTrigger(context);
		context.OnEvent(GameEvents.GameStart, _hudTrigger.ExecuteAsync);
		context.OnEvent(GameEvents.GameStart, worldTrigger.ExecuteAsync);
		context.OnEvent(GameEvents.MapLoaded, worldTrigger.HandleMapLoadedAsync);
	}

	public void OnUnload()
	{
		_hudTrigger?.DisposeInstallation();
		_hudTrigger = null;
	}
}
