using System;
using System.Collections.Generic;
using System.Numerics;
using Ludots.Core.Presentation.Components;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Presentation.Rendering
{
    /// <summary>
    /// Frame-persistent cache of the latest visual proxies keyed by StableId.
    /// Request flush updates the cache; frame projection replays it into adapter-facing
    /// buffers. This keeps authoring/runtime semantics unchanged while introducing
    /// the Layer 5 cache boundary required by compiled lanes.
    /// </summary>
    public sealed class StableDrawCache
    {
        private readonly Dictionary<int, int> _slotByStableId;
        private PresentationVisualProxy[] _entries;
        private int[] _frameTouched;
        private PrimitiveDrawItem[] _staticMeshDeltaItems = Array.Empty<PrimitiveDrawItem>();
        private int[] _staticMeshRemovedStableIds = Array.Empty<int>();
        private int _count;
        private int _frameStamp;
        private int _contentRevision;
        private int _nonStaticContentRevision;
        private int _staticMeshGeometryRevision;
        private int _staticMeshDeltaBaseRevision;
        private int _staticMeshDeltaItemCount;
        private int _staticMeshRemovedStableIdCount;

        public int Count => _count;
        public int ContentRevision => _contentRevision;
        public int NonStaticContentRevision => _nonStaticContentRevision;
        public int StaticMeshGeometryRevision => _staticMeshGeometryRevision;
        public int StaticMeshDeltaBaseRevision => _staticMeshDeltaBaseRevision;
        public ReadOnlySpan<PrimitiveDrawItem> StaticMeshDeltaItems =>
            new ReadOnlySpan<PrimitiveDrawItem>(_staticMeshDeltaItems, 0, _staticMeshDeltaItemCount);

        public ReadOnlySpan<int> StaticMeshRemovedStableIds =>
            new ReadOnlySpan<int>(_staticMeshRemovedStableIds, 0, _staticMeshRemovedStableIdCount);

        public StableDrawCache(int capacity = 131072)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            _slotByStableId = new Dictionary<int, int>(capacity);
            _entries = new PresentationVisualProxy[capacity];
            _frameTouched = new int[capacity];
        }

        public void BeginFrame()
        {
            if (_frameStamp == int.MaxValue)
            {
                Array.Clear(_frameTouched, 0, _frameTouched.Length);
                _frameStamp = 0;
            }

            _frameStamp++;
        }

        public void Upsert(in PresentationVisualProxy proxy)
        {
            if (_slotByStableId.TryGetValue(proxy.StableId, out int existing))
            {
                PresentationVisualProxy current = _entries[existing];
                if (!ProxyEquals(current, proxy))
                {
                    _contentRevision++;
                    if (!proxy.RenderPath.IsStaticInstanceLane() ||
                        !current.RenderPath.IsStaticInstanceLane())
                    {
                        _nonStaticContentRevision++;
                    }
                }

                if (!StaticMeshLaneStateEquals(current, proxy))
                {
                    _staticMeshGeometryRevision++;
                    TrackStaticMeshDelta(in proxy);
                }

                _entries[existing] = proxy;
                _frameTouched[existing] = _frameStamp;
                return;
            }

            EnsureCapacity(_count + 1);
            int slot = _count++;
            _slotByStableId.Add(proxy.StableId, slot);
            _entries[slot] = proxy;
            _frameTouched[slot] = _frameStamp;
            _contentRevision++;
            if (proxy.RenderPath.IsStaticInstanceLane())
            {
                _staticMeshGeometryRevision++;
                TrackStaticMeshDelta(in proxy);
            }
            else
            {
                _nonStaticContentRevision++;
            }
        }

        public void AddNew(in PresentationVisualProxy proxy)
        {
            if (_slotByStableId.ContainsKey(proxy.StableId))
            {
                throw new InvalidOperationException(
                    $"StableDrawCache already contains stableId={proxy.StableId}. AddNew is reserved for first-time static visual insertion.");
            }

            EnsureCapacity(_count + 1);
            int slot = _count++;
            _slotByStableId.Add(proxy.StableId, slot);
            _entries[slot] = proxy;
            _frameTouched[slot] = _frameStamp;
            _contentRevision++;
            if (proxy.RenderPath.IsStaticInstanceLane())
            {
                _staticMeshGeometryRevision++;
                TrackStaticMeshDelta(in proxy);
            }
            else
            {
                _nonStaticContentRevision++;
            }
        }

        public void Remove(int stableId)
        {
            if (!_slotByStableId.TryGetValue(stableId, out int slot))
            {
                return;
            }

            RemoveAt(slot);
        }

        public bool Contains(int stableId)
        {
            return _slotByStableId.ContainsKey(stableId);
        }

        public bool UpdatePosition(int stableId, Vector3 newPosition)
        {
            if (!_slotByStableId.TryGetValue(stableId, out int slot))
            {
                return false;
            }

            if (_entries[slot].Position == newPosition)
            {
                _frameTouched[slot] = _frameStamp;
                return true;
            }

            _entries[slot].Position = newPosition;
            _frameTouched[slot] = _frameStamp;
            _contentRevision++;
            if (_entries[slot].RenderPath.IsStaticInstanceLane())
            {
                _staticMeshGeometryRevision++;
                TrackStaticMeshDelta(in _entries[slot]);
            }
            else
            {
                _nonStaticContentRevision++;
            }
            return true;
        }

        public void Project(PresentationVisualProxyEmitter emitter, bool evictUntouched)
        {
            if (emitter == null)
            {
                throw new ArgumentNullException(nameof(emitter));
            }

            int index = 0;
            while (index < _count)
            {
                if (evictUntouched && _frameTouched[index] != _frameStamp)
                {
                    RemoveAt(index);
                    continue;
                }

                emitter.Emit(_entries[index]);
                index++;
            }
        }

        public void Clear()
        {
            _slotByStableId.Clear();
            _count = 0;
            _frameStamp = 0;
            _contentRevision = 0;
            _nonStaticContentRevision = 0;
            _staticMeshGeometryRevision = 0;
            _staticMeshDeltaBaseRevision = 0;
            _staticMeshDeltaItemCount = 0;
            _staticMeshRemovedStableIdCount = 0;
            Array.Clear(_frameTouched, 0, _frameTouched.Length);
        }

        public void ClearStaticMeshDeltas()
        {
            _staticMeshDeltaItemCount = 0;
            _staticMeshRemovedStableIdCount = 0;
            _staticMeshDeltaBaseRevision = _contentRevision;
        }

        private void EnsureCapacity(int required)
        {
            if (required <= _entries.Length)
            {
                return;
            }

            int next = _entries.Length * 2;
            if (next < required)
            {
                next = required;
            }

            Array.Resize(ref _entries, next);
            Array.Resize(ref _frameTouched, next);
        }

        private void RemoveAt(int slot)
        {
            int last = _count - 1;
            int removedStableId = _entries[slot].StableId;
            if (_entries[slot].RenderPath.IsStaticInstanceLane())
            {
                _staticMeshGeometryRevision++;
                TrackStaticMeshRemoval(_entries[slot].StableId);
            }
            else
            {
                _nonStaticContentRevision++;
            }

            _slotByStableId.Remove(removedStableId);

            if (slot != last)
            {
                PresentationVisualProxy moved = _entries[last];
                _entries[slot] = moved;
                _frameTouched[slot] = _frameTouched[last];
                _slotByStableId[moved.StableId] = slot;
            }

            _entries[last] = default;
            _frameTouched[last] = 0;
            _count = last;
            _contentRevision++;
        }

        private static bool ProxyEquals(in PresentationVisualProxy a, in PresentationVisualProxy b)
        {
            return a.ProxyKind == b.ProxyKind
                && a.Payload.Equals(b.Payload)
                && a.Mobility == b.Mobility
                && a.Flags == b.Flags
                && a.LOD == b.LOD;
        }

        private static bool StaticMeshLaneStateEquals(in PresentationVisualProxy a, in PresentationVisualProxy b)
        {
            PrimitiveDrawItem itemA = ToPrimitive(a);
            PrimitiveDrawItem itemB = ToPrimitive(b);
            bool supportsA = itemA.RenderPath.IsStaticInstanceLane();
            bool supportsB = itemB.RenderPath.IsStaticInstanceLane();
            if (!supportsA || !supportsB)
            {
                return supportsA == supportsB;
            }

            return itemA.Payload.Equals(itemB.Payload)
                && itemA.Mobility == itemB.Mobility
                && itemA.Flags == itemB.Flags
                && itemA.LOD == itemB.LOD;
        }

        private static PrimitiveDrawItem ToPrimitive(in PresentationVisualProxy proxy)
        {
            return new PrimitiveDrawItem
            {
                Payload = proxy.Payload,
                Mobility = proxy.Mobility,
                Flags = proxy.Flags,
                LOD = proxy.LOD,
            };
        }

        private void TrackStaticMeshDelta(in PresentationVisualProxy proxy)
        {
            if (!proxy.RenderPath.IsStaticInstanceLane())
            {
                return;
            }

            EnsureDeltaItemCapacity(_staticMeshDeltaItemCount + 1);
            _staticMeshDeltaItems[_staticMeshDeltaItemCount++] = ToPrimitive(proxy);
        }

        private void TrackStaticMeshRemoval(int stableId)
        {
            if (stableId <= 0)
            {
                return;
            }

            EnsureRemovedStableIdCapacity(_staticMeshRemovedStableIdCount + 1);
            _staticMeshRemovedStableIds[_staticMeshRemovedStableIdCount++] = stableId;
        }

        private void EnsureDeltaItemCapacity(int required)
        {
            if (required <= _staticMeshDeltaItems.Length)
            {
                return;
            }

            int next = _staticMeshDeltaItems.Length == 0 ? 16 : _staticMeshDeltaItems.Length * 2;
            if (next < required)
            {
                next = required;
            }

            Array.Resize(ref _staticMeshDeltaItems, next);
        }

        private void EnsureRemovedStableIdCapacity(int required)
        {
            if (required <= _staticMeshRemovedStableIds.Length)
            {
                return;
            }

            int next = _staticMeshRemovedStableIds.Length == 0 ? 16 : _staticMeshRemovedStableIds.Length * 2;
            if (next < required)
            {
                next = required;
            }

            Array.Resize(ref _staticMeshRemovedStableIds, next);
        }
    }
}
