using Ludots.Core.Modding;

namespace PanelKitTooltipShowcaseMod;

public sealed class PanelKitTooltipShowcaseModEntry : IMod
{
	public void OnLoad(IModContext context)
	{
		ArgumentNullException.ThrowIfNull(context);
		context.Log("[PanelKitTooltipShowcaseMod] Loaded - WPK-10 tooltip panel showcase (authoring profiles under Assets/PanelKit).");
	}

	public void OnUnload()
	{
	}
}
