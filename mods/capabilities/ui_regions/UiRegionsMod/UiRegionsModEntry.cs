using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using UiRegionsMod.Triggers;

namespace UiRegionsMod;

public sealed class UiRegionsModEntry : IMod
{
	public void OnLoad(IModContext context)
	{
		ArgumentNullException.ThrowIfNull(context);
		context.Log("[UiRegionsMod] Loaded.");
		context.OnEvent(GameEvents.GameStart, new InstallUiRegionsOnGameStartTrigger(context).ExecuteAsync);
	}

	public void OnUnload()
	{
	}
}
