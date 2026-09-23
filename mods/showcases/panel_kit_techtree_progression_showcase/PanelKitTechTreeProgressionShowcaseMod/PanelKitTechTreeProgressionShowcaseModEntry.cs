using Ludots.Core.Modding;

namespace PanelKitTechTreeProgressionShowcaseMod;

public sealed class PanelKitTechTreeProgressionShowcaseModEntry : IMod
{
	public void OnLoad(IModContext context)
	{
		ArgumentNullException.ThrowIfNull(context);
		context.Log("[PanelKitTechTreeProgressionShowcaseMod] Loaded - WPK-10 techtree panel showcase (authoring profiles under Assets/PanelKit).");
	}

	public void OnUnload()
	{
	}
}
