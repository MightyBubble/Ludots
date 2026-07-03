namespace Ludots.UI.Surface;

public readonly record struct UiSurfaceLeaseHandle(string OwnerId, long LeaseId, int Generation)
{
	public bool IsValid => !string.IsNullOrWhiteSpace(OwnerId) && LeaseId > 0 && Generation > 0;
}
