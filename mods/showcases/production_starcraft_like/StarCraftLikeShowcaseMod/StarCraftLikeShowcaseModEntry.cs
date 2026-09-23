using Ludots.Core.Modding;

namespace StarCraftLikeShowcaseMod;

public sealed class StarCraftLikeShowcaseModEntry : IMod
{
	public void OnLoad(IModContext context) => context.Log("[StarCraftLikeShowcaseMod] Loaded");
	public void OnUnload() { }
}
