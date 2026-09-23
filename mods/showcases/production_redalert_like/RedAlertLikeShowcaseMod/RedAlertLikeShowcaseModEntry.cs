using Ludots.Core.Modding;

namespace RedAlertLikeShowcaseMod;

public sealed class RedAlertLikeShowcaseModEntry : IMod
{
	public void OnLoad(IModContext context) => context.Log("[RedAlertLikeShowcaseMod] Loaded");
	public void OnUnload() { }
}
