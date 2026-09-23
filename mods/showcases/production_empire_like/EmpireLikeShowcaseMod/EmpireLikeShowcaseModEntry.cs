using Ludots.Core.Modding;

namespace EmpireLikeShowcaseMod;

public sealed class EmpireLikeShowcaseModEntry : IMod
{
	public void OnLoad(IModContext context) => context.Log("[EmpireLikeShowcaseMod] Loaded");
	public void OnUnload() { }
}
