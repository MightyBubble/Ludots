using Ludots.Core.Modding;

namespace UiRegionsMod;

/// <summary>
/// The mod ships the region catalog and HUD binder as a reusable capability; installation is
/// consumer-driven via <see cref="Runtime.UiRegionsHudInstaller.Install"/>, which binds the
/// catalog to a real DataPlane topic registry.
/// </summary>
public sealed class UiRegionsModEntry : IMod
{
	public void OnLoad(IModContext context)
	{
		ArgumentNullException.ThrowIfNull(context);
		context.Log("[UiRegionsMod] Loaded.");
	}

	public void OnUnload()
	{
	}
}
