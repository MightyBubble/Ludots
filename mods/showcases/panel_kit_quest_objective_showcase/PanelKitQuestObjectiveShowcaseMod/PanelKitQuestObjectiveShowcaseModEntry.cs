using Ludots.Core.Modding;

namespace PanelKitQuestObjectiveShowcaseMod;

public sealed class PanelKitQuestObjectiveShowcaseModEntry : IMod
{
	public void OnLoad(IModContext context)
	{
		ArgumentNullException.ThrowIfNull(context);
		context.Log("[PanelKitQuestObjectiveShowcaseMod] Loaded - WPK-10 objective panel showcase (authoring profiles under Assets/PanelKit).");
	}

	public void OnUnload()
	{
	}
}
