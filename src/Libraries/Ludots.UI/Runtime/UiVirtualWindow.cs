namespace Ludots.UI.Runtime;

public readonly record struct UiVirtualWindow(
	string HostElementId,
	int TotalCount,
	int StartIndex,
	int EndIndexExclusive,
	float ItemExtent,
	float ViewportExtent,
	float ScrollOffset,
	float LeadingSpacerExtent,
	float TrailingSpacerExtent)
{
	public int VisibleCount => EndIndexExclusive > StartIndex ? EndIndexExclusive - StartIndex : 0;

	public static UiVirtualWindow Empty(string hostElementId, float itemExtent, float viewportExtent)
	{
		return new UiVirtualWindow(hostElementId, 0, 0, 0, itemExtent, viewportExtent, 0f, 0f, 0f);
	}
}
