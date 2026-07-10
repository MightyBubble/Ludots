using Ludots.Core.Modding;

namespace PanelKitNotificationShowcaseMod;

public sealed class PanelKitNotificationShowcaseModEntry : IMod
{
	public void OnLoad(IModContext context)
	{
		ArgumentNullException.ThrowIfNull(context);
		context.Log("[PanelKitNotificationShowcaseMod] Loaded - WPK-10 notification panel showcase (authoring profiles under Assets/PanelKit).");
	}

	public void OnUnload()
	{
	}
}
