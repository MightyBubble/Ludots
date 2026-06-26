using System;

namespace Ludots.UI.Surface;

public sealed class UiSurfaceLeaseRequest
{
	public string OwnerId { get; }

	public UiSurfaceSegment Segment { get; }

	public int Priority { get; }

	public bool Exclusive { get; }

	public UiSurfaceLeaseRequest(string ownerId, UiSurfaceSegment segment = UiSurfaceSegment.Main, int priority = 0, bool exclusive = false)
	{
		if (string.IsNullOrWhiteSpace(ownerId))
		{
			throw new ArgumentException("UI surface owner id is required.", nameof(ownerId));
		}

		OwnerId = ownerId.Trim();
		Segment = segment;
		Priority = priority;
		Exclusive = exclusive;
	}
}
