using System;
using System.Collections.Generic;
using System.Numerics;
using Ludots.Core.Presentation.Components;

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
        private int _count;
        private int _frameStamp;
        private int _contentRevision;
        private int _staticMeshGeometryRevision;

        public int Count => _count;
        public int ContentRevision => _contentRevision;
        public int StaticMeshGeometryRevision => _staticMeshGeometryRevision;

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
                }

                if (!StaticMeshLaneStateEquals(current, proxy))
                {
                    _staticMeshGeometryRevision++;
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
            }
        }

        public void AddNew(in PresentationVisualProxy proxy)
        {
            EnsureCapacity(_count + 1);
            int slot = _count++;
            _slotByStableId.Add(proxy.StableId, slot);
            _entries[slot] = proxy;
            _frameTouched[slot] = _frameStamp;
            _contentRevision++;
            if (proxy.RenderPath.IsStaticInstanceLane())
            {
                _staticMeshGeometryRevision++;
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
            _staticMeshGeometryRevision = 0;
            Array.Clear(_frameTouched, 0, _frameTouched.Length);
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

            return itemA.MeshAssetId == itemB.MeshAssetId
                && itemA.MaterialId == itemB.MaterialId
                && itemA.RenderPath == itemB.RenderPath
                && itemA.Mobility == itemB.Mobility
                && itemA.Position.Equals(itemB.Position)
                && itemA.Rotation.Equals(itemB.Rotation)
                && itemA.Scale.Equals(itemB.Scale)
                && itemA.Visibility == itemB.Visibility;
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
    }
}
