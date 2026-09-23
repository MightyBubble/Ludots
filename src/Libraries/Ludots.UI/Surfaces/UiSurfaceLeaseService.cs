using System;
using System.Collections.Generic;

namespace Ludots.UI.Surfaces;

public enum UiSurfaceKind : byte
{
	RetainedUi = 0,
	ScreenOverlay = 1,
	WorldHud = 2,
	AdapterOverlay = 3,
}

public readonly record struct UiSurfaceLeaseHandle(int Slot, uint Generation)
{
	public bool IsValid => Slot >= 0 && Generation != 0;
	public static UiSurfaceLeaseHandle Invalid { get; } = new(-1, 0);
}

public readonly record struct UiSurfaceLeaseRequest(
	UiSurfaceKind Surface,
	string SegmentId,
	string OwnerId,
	bool Exclusive);

public interface IUiSurfaceLeaseService
{
	UiSurfaceLeaseHandle Acquire(in UiSurfaceLeaseRequest request);
	bool TryRevalidate(UiSurfaceLeaseHandle handle);
	bool Release(UiSurfaceLeaseHandle handle);
	bool TryGetOwner(UiSurfaceKind surface, string segmentId, out string ownerId);
}

public sealed class UiSurfaceLeaseService : IUiSurfaceLeaseService
{
	private readonly Dictionary<UiSurfaceKey, int> _slotsByKey = new();
	private Slot[] _slots = new Slot[16];
	private int _count;

	public UiSurfaceLeaseHandle Acquire(in UiSurfaceLeaseRequest request)
	{
		Validate(request);
		var key = new UiSurfaceKey(request.Surface, request.SegmentId);
		if (_slotsByKey.TryGetValue(key, out int existingSlot))
		{
			ref Slot existing = ref _slots[existingSlot];
			if (!string.Equals(existing.OwnerId, request.OwnerId, StringComparison.Ordinal))
			{
				throw new InvalidOperationException(
					$"UI surface '{request.Surface}:{request.SegmentId}' is already owned by '{existing.OwnerId}'.");
			}

			return new UiSurfaceLeaseHandle(existingSlot, existing.Generation);
		}

		int slot = _count++;
		if (slot >= _slots.Length)
		{
			Array.Resize(ref _slots, _slots.Length * 2);
		}

		_slots[slot] = new Slot(
			request.Surface,
			request.SegmentId,
			request.OwnerId,
			request.Exclusive,
			1);
		_slotsByKey.Add(key, slot);
		return new UiSurfaceLeaseHandle(slot, 1);
	}

	public bool TryRevalidate(UiSurfaceLeaseHandle handle)
	{
		if (!TryGetSlot(handle, out Slot slot))
		{
			return false;
		}

		return slot.Generation == handle.Generation;
	}

	public bool Release(UiSurfaceLeaseHandle handle)
	{
		if (!TryGetSlot(handle, out Slot slot) || slot.Generation != handle.Generation)
		{
			return false;
		}

		_slotsByKey.Remove(new UiSurfaceKey(slot.Surface, slot.SegmentId));
		_slots[handle.Slot] = slot with { OwnerId = string.Empty, SegmentId = string.Empty, Generation = slot.Generation + 1 };
		return true;
	}

	public bool TryGetOwner(UiSurfaceKind surface, string segmentId, out string ownerId)
	{
		ownerId = string.Empty;
		if (string.IsNullOrWhiteSpace(segmentId))
		{
			return false;
		}

		if (!_slotsByKey.TryGetValue(new UiSurfaceKey(surface, segmentId.Trim()), out int slot))
		{
			return false;
		}

		ownerId = _slots[slot].OwnerId;
		return !string.IsNullOrEmpty(ownerId);
	}

	private bool TryGetSlot(UiSurfaceLeaseHandle handle, out Slot slot)
	{
		slot = default;
		if (!handle.IsValid || (uint)handle.Slot >= (uint)_count)
		{
			return false;
		}

		slot = _slots[handle.Slot];
		return !string.IsNullOrEmpty(slot.OwnerId);
	}

	private static void Validate(in UiSurfaceLeaseRequest request)
	{
		if (string.IsNullOrWhiteSpace(request.SegmentId))
		{
			throw new ArgumentException("UI surface segment id is required.", nameof(request));
		}

		if (string.IsNullOrWhiteSpace(request.OwnerId))
		{
			throw new ArgumentException("UI surface owner id is required.", nameof(request));
		}
	}

	private readonly record struct UiSurfaceKey(UiSurfaceKind Surface, string SegmentId);

	private readonly record struct Slot(
		UiSurfaceKind Surface,
		string SegmentId,
		string OwnerId,
		bool Exclusive,
		uint Generation);
}
