using Ludots.Core.Modding;

namespace FourXLikeShowcaseMod;

public sealed class FourXLikeShowcaseModEntry : IMod
{
	public void OnLoad(IModContext context) => context.Log("[FourXLikeShowcaseMod] Loaded");
	public void OnUnload() { }
}
