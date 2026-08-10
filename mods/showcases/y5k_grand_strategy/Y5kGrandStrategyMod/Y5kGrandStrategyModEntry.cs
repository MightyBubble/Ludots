using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using Y5kGrandStrategyMod.Triggers;

namespace Y5kGrandStrategyMod;

public sealed class Y5kGrandStrategyModEntry : IMod
{
	private InstallY5kHudOnGameStartTrigger? _hudTrigger;
	private InstallY5kWorldOnGameStartTrigger? _worldTrigger;

	public void OnLoad(IModContext context)
	{
		ArgumentNullException.ThrowIfNull(context);
		context.Log("[Y5kGrandStrategyMod] Loaded — content + HUD assembly only.");
		_worldTrigger = new InstallY5kWorldOnGameStartTrigger(context);
		_hudTrigger = new InstallY5kHudOnGameStartTrigger(context);

		// Seed world/objectives first on MapLoaded, then bind/refresh HUD so panels show live data.
		context.OnEvent(GameEvents.GameStart, _worldTrigger.ExecuteAsync);
		context.OnEvent(GameEvents.MapLoaded, async ctx =>
		{
			await _worldTrigger.HandleMapLoadedAsync(ctx).ConfigureAwait(false);
			await _hudTrigger.ExecuteAsync(ctx).ConfigureAwait(false);
		});
	}

	public void OnUnload()
	{
		_hudTrigger?.DisposeInstallation();
		_hudTrigger = null;
		_worldTrigger = null;
	}
}
