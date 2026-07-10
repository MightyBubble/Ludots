using Ludots.Core.Modding;

namespace PanelKitProductionWorkerShowcaseMod;

public sealed class PanelKitProductionWorkerShowcaseModEntry : IMod
{
	public void OnLoad(IModContext context)
	{
		ArgumentNullException.ThrowIfNull(context);
		context.Log("[PanelKitProductionWorkerShowcaseMod] Loaded - WPK-10 production-overview panel showcase (authoring profiles under Assets/PanelKit).");
	}

	public void OnUnload()
	{
	}
}
