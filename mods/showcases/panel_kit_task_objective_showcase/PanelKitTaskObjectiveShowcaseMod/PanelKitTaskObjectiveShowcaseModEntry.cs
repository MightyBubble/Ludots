using Ludots.Core.Modding;

namespace PanelKitTaskObjectiveShowcaseMod;

public sealed class PanelKitTaskObjectiveShowcaseModEntry : IMod
{
	public void OnLoad(IModContext context)
	{
		ArgumentNullException.ThrowIfNull(context);
		context.Log("[PanelKitTaskObjectiveShowcaseMod] Loaded - WPK-10 objective panel showcase (authoring profiles under Assets/PanelKit).");
	}

	public void OnUnload()
	{
	}
}
