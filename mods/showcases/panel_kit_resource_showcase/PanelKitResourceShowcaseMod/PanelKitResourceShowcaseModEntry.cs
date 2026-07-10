using Ludots.Core.Modding;

namespace PanelKitResourceShowcaseMod;

public sealed class PanelKitResourceShowcaseModEntry : IMod
{
	public void OnLoad(IModContext context)
	{
		ArgumentNullException.ThrowIfNull(context);
		context.Log("[PanelKitResourceShowcaseMod] Loaded - WPK-10 resource-bar panel showcase (authoring profiles under Assets/PanelKit).");
	}

	public void OnUnload()
	{
	}
}
