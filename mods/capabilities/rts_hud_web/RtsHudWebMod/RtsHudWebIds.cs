using Ludots.Core.Scripting;
using Ludots.UI.Surfaces;

namespace RtsHudWebMod;

public static class RtsHudWebIds
{
	public const string InstalledKey = "RtsHudWebMod.Installed";
	public const string OwnerId = "RtsHudWebMod";
	public const string MainSegment = "main-hud";
}

public static class RtsHudWebServiceKeys
{
	public static readonly ServiceKey<IUiSurfaceLeaseService> UiSurfaceLeaseService = new("UiSurfaceLeaseService");
}
