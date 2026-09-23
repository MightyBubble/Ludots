using Ludots.Core.Modding;

namespace PanelKitCommandDeckShowcaseMod;

public sealed class PanelKitCommandDeckShowcaseModEntry : IMod
{
	public void OnLoad(IModContext context)
	{
		ArgumentNullException.ThrowIfNull(context);
		context.Log("[PanelKitCommandDeckShowcaseMod] Loaded - WPK-10 command-deck panel showcase (authoring profiles under Assets/PanelKit).");
	}

	public void OnUnload()
	{
	}
}
