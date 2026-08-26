using System;
using Arch.Core;
using Ludots.Core.Presentation.Rendering;

namespace Ludots.Core.Presentation.Requests
{
    public sealed class PresentationRequestBuffer
    {
        private readonly VisualProxyChannelItem[] _visualProxies;
        private readonly GroundOverlayChannelItem[] _groundOverlays;
        private readonly WorldHudChannelItem[] _worldHud;
        private readonly SplineRibbonChannelItem[] _splineRibbons;
        private readonly SurfaceSourceChannelItem[] _surfaceSources;
        private readonly PresentationRemovalRequest[] _removals;
        private readonly Entity[] _clearTransients;
        private readonly PresentationRequestOp[] _ops;
        private PresentationRequest[]? _spanScratch;
        private int _visualProxyCount;
        private int _groundOverlayCount;
        private int _worldHudCount;
        private int _splineRibbonCount;
        private int _surfaceSourceCount;
        private int _removalCount;
        private int _clearTransientCount;
        private int _opCount;

        public PresentationRequestBuffer(int capacity = 131072)
            : this(PresentationRequestChannelCapacities.Uniform(capacity))
        {
        }

        public PresentationRequestBuffer(in PresentationRequestChannelCapacities capacities)
        {
            _visualProxies = new VisualProxyChannelItem[capacities.VisualProxy];
            _groundOverlays = new GroundOverlayChannelItem[capacities.GroundOverlay];
            _worldHud = new WorldHudChannelItem[capacities.WorldHud];
            _splineRibbons = new SplineRibbonChannelItem[capacities.SplineRibbon];
            _surfaceSources = new SurfaceSourceChannelItem[capacities.SurfaceSource];
            _removals = new PresentationRemovalRequest[capacities.Removal];
            _clearTransients = new Entity[capacities.ClearTransient];
            _ops = new PresentationRequestOp[capacities.TotalOperationCapacity];
        }

        public int Count => _opCount;

        public int Capacity => _ops.Length;

        internal ReadOnlySpan<PresentationRequestOp> Ops => _ops.AsSpan(0, _opCount);

        internal int ClearTransientCount => _clearTransientCount;

        internal ReadOnlySpan<VisualProxyChannelItem> VisualProxies => _visualProxies.AsSpan(0, _visualProxyCount);

        public void Add(in PresentationRequest request)
        {
            switch (request.Kind)
            {
                case PresentationRequestKind.VisualProxy:
                    AddVisualProxy(in request);
                    break;
                case PresentationRequestKind.GroundOverlay:
                    AddGroundOverlay(in request);
                    break;
                case PresentationRequestKind.WorldHud:
                    AddWorldHud(in request);
                    break;
                case PresentationRequestKind.SplineRibbon:
                    AddSplineRibbon(in request);
                    break;
                case PresentationRequestKind.SurfaceSource:
                    AddSurfaceSource(in request);
                    break;
                case PresentationRequestKind.RemoveGroundOverlay:
                case PresentationRequestKind.RemoveWorldHud:
                case PresentationRequestKind.RemoveSplineRibbon:
                case PresentationRequestKind.RemoveSurfaceSource:
                    AddRemoval(in request);
                    break;
                case PresentationRequestKind.ClearTransientVisualProjection:
                    AddClearTransient(in request);
                    break;
                default:
                    throw new InvalidOperationException($"Unknown PresentationRequestKind '{request.Kind}'.");
            }
        }

        public ReadOnlySpan<PresentationRequest> GetSpan()
        {
            if (_opCount == 0)
            {
                return ReadOnlySpan<PresentationRequest>.Empty;
            }

            if (_spanScratch == null || _spanScratch.Length < _opCount)
            {
                _spanScratch = new PresentationRequest[_opCount];
            }

            for (int i = 0; i < _opCount; i++)
            {
                _spanScratch[i] = Reconstruct(i);
            }

            return _spanScratch.AsSpan(0, _opCount);
        }

        public PresentationRequest Get(int index)
        {
            if ((uint)index >= (uint)_opCount)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return Reconstruct(index);
        }

        public void Clear()
        {
            _visualProxyCount = 0;
            _groundOverlayCount = 0;
            _worldHudCount = 0;
            _splineRibbonCount = 0;
            _surfaceSourceCount = 0;
            _removalCount = 0;
            _clearTransientCount = 0;
            _opCount = 0;
        }

        internal ref readonly GroundOverlayChannelItem GroundOverlayAt(int slot) => ref _groundOverlays[slot];

        internal ref readonly WorldHudChannelItem WorldHudAt(int slot) => ref _worldHud[slot];

        internal ref readonly SplineRibbonChannelItem SplineRibbonAt(int slot) => ref _splineRibbons[slot];

        internal ref readonly SurfaceSourceChannelItem SurfaceSourceAt(int slot) => ref _surfaceSources[slot];

        internal ref readonly PresentationRemovalRequest RemovalAt(int slot) => ref _removals[slot];

        internal ref readonly VisualProxyChannelItem VisualProxyAt(int slot) => ref _visualProxies[slot];

        private void AddVisualProxy(in PresentationRequest request)
        {
            EnsureOperationRoom(request.Kind, request.VisualProxy.StableId);
            EnsureChannelRoom(_visualProxyCount, _visualProxies.Length, request.Kind, request.VisualProxy.StableId);
            int slot = _visualProxyCount++;
            _visualProxies[slot] = new VisualProxyChannelItem
            {
                Owner = request.Owner,
                VisualProxy = request.VisualProxy,
            };
            RecordOp(PresentationRequestChannel.VisualProxy, slot, request.Kind, request.VisualProxy.StableId);
        }

        private void AddGroundOverlay(in PresentationRequest request)
        {
            EnsureOperationRoom(request.Kind, request.GroundOverlay.StableId);
            EnsureChannelRoom(_groundOverlayCount, _groundOverlays.Length, request.Kind, request.GroundOverlay.StableId);
            int slot = _groundOverlayCount++;
            _groundOverlays[slot] = new GroundOverlayChannelItem
            {
                Owner = request.Owner,
                LOD = request.LOD,
                Item = request.GroundOverlay,
            };
            RecordOp(PresentationRequestChannel.GroundOverlay, slot, request.Kind, request.GroundOverlay.StableId);
        }

        private void AddWorldHud(in PresentationRequest request)
        {
            EnsureOperationRoom(request.Kind, request.WorldHud.StableId);
            EnsureChannelRoom(_worldHudCount, _worldHud.Length, request.Kind, request.WorldHud.StableId);
            int slot = _worldHudCount++;
            _worldHud[slot] = new WorldHudChannelItem
            {
                Owner = request.Owner,
                LOD = request.LOD,
                Item = request.WorldHud,
            };
            RecordOp(PresentationRequestChannel.WorldHud, slot, request.Kind, request.WorldHud.StableId);
        }

        private void AddSplineRibbon(in PresentationRequest request)
        {
            EnsureOperationRoom(request.Kind, request.SplineRibbon.StableId);
            EnsureChannelRoom(_splineRibbonCount, _splineRibbons.Length, request.Kind, request.SplineRibbon.StableId);
            int slot = _splineRibbonCount++;
            _splineRibbons[slot] = new SplineRibbonChannelItem
            {
                Owner = request.Owner,
                LOD = request.LOD,
                Item = request.SplineRibbon,
            };
            RecordOp(PresentationRequestChannel.SplineRibbon, slot, request.Kind, request.SplineRibbon.StableId);
        }

        private void AddSurfaceSource(in PresentationRequest request)
        {
            EnsureOperationRoom(request.Kind, request.SurfaceSource.StableId);
            EnsureChannelRoom(_surfaceSourceCount, _surfaceSources.Length, request.Kind, request.SurfaceSource.StableId);
            int slot = _surfaceSourceCount++;
            _surfaceSources[slot] = new SurfaceSourceChannelItem
            {
                Owner = request.Owner,
                LOD = request.LOD,
                Item = request.SurfaceSource,
            };
            RecordOp(PresentationRequestChannel.SurfaceSource, slot, request.Kind, request.SurfaceSource.StableId);
        }

        public void RemoveGroundOverlay(Entity owner, int stableId) =>
            AddRemoval(owner, PresentationRequestKind.RemoveGroundOverlay, stableId);

        public void RemoveWorldHud(Entity owner, int stableId) =>
            AddRemoval(owner, PresentationRequestKind.RemoveWorldHud, stableId);

        public void RemoveSplineRibbon(Entity owner, int stableId) =>
            AddRemoval(owner, PresentationRequestKind.RemoveSplineRibbon, stableId);

        public void RemoveSurfaceSource(Entity owner, int stableId) =>
            AddRemoval(owner, PresentationRequestKind.RemoveSurfaceSource, stableId);

        private void AddRemoval(in PresentationRequest request)
        {
            AddRemoval(request.Owner, request.Kind, request.StableId);
        }

        private void AddRemoval(Entity owner, PresentationRequestKind kind, int stableId)
        {
            EnsureOperationRoom(kind, stableId);
            EnsureChannelRoom(_removalCount, _removals.Length, kind, stableId);
            int slot = _removalCount++;
            _removals[slot] = new PresentationRemovalRequest
            {
                Kind = kind,
                Owner = owner,
                StableId = stableId,
            };
            RecordOp(PresentationRequestChannel.Removal, slot, kind, stableId);
        }

        private void AddClearTransient(in PresentationRequest request)
        {
            EnsureOperationRoom(request.Kind, request.StableId);
            EnsureChannelRoom(_clearTransientCount, _clearTransients.Length, request.Kind, request.StableId);
            int slot = _clearTransientCount++;
            _clearTransients[slot] = request.Owner;
            RecordOp(PresentationRequestChannel.ClearTransient, slot, request.Kind, request.StableId);
        }

        private void EnsureOperationRoom(PresentationRequestKind kind, int stableId)
        {
            if (_opCount >= _ops.Length)
            {
                throw new InvalidOperationException(
                    $"PresentationRequestBuffer overflowed while adding kind={kind}, stableId={stableId}.");
            }
        }

        private void RecordOp(PresentationRequestChannel channel, int slot, PresentationRequestKind kind, int stableId)
        {
            EnsureOperationRoom(kind, stableId);
            _ops[_opCount++] = new PresentationRequestOp(channel, slot);
        }

        private static void EnsureChannelRoom(int count, int capacity, PresentationRequestKind kind, int stableId)
        {
            if (count >= capacity)
            {
                throw new InvalidOperationException(
                    $"PresentationRequestBuffer overflowed while adding kind={kind}, stableId={stableId}.");
            }
        }

        private PresentationRequest Reconstruct(int opIndex)
        {
            PresentationRequestOp op = _ops[opIndex];
            switch (op.Channel)
            {
                case PresentationRequestChannel.VisualProxy:
                {
                    ref readonly VisualProxyChannelItem item = ref _visualProxies[op.Slot];
                    return new PresentationRequest
                    {
                        Kind = PresentationRequestKind.VisualProxy,
                        Owner = item.Owner,
                        LOD = item.VisualProxy.LOD,
                        VisualProxy = item.VisualProxy,
                    };
                }
                case PresentationRequestChannel.GroundOverlay:
                {
                    ref readonly GroundOverlayChannelItem item = ref _groundOverlays[op.Slot];
                    return new PresentationRequest
                    {
                        Kind = PresentationRequestKind.GroundOverlay,
                        Owner = item.Owner,
                        LOD = item.LOD,
                        GroundOverlay = item.Item,
                    };
                }
                case PresentationRequestChannel.WorldHud:
                {
                    ref readonly WorldHudChannelItem item = ref _worldHud[op.Slot];
                    return new PresentationRequest
                    {
                        Kind = PresentationRequestKind.WorldHud,
                        Owner = item.Owner,
                        LOD = item.LOD,
                        WorldHud = item.Item,
                    };
                }
                case PresentationRequestChannel.SplineRibbon:
                {
                    ref readonly SplineRibbonChannelItem item = ref _splineRibbons[op.Slot];
                    return new PresentationRequest
                    {
                        Kind = PresentationRequestKind.SplineRibbon,
                        Owner = item.Owner,
                        LOD = item.LOD,
                        SplineRibbon = item.Item,
                    };
                }
                case PresentationRequestChannel.SurfaceSource:
                {
                    ref readonly SurfaceSourceChannelItem item = ref _surfaceSources[op.Slot];
                    return new PresentationRequest
                    {
                        Kind = PresentationRequestKind.SurfaceSource,
                        Owner = item.Owner,
                        LOD = item.LOD,
                        StableId = item.Item.StableId,
                        SurfaceSource = item.Item,
                    };
                }
                case PresentationRequestChannel.Removal:
                {
                    ref readonly PresentationRemovalRequest item = ref _removals[op.Slot];
                    return new PresentationRequest
                    {
                        Kind = item.Kind,
                        Owner = item.Owner,
                        StableId = item.StableId,
                    };
                }
                case PresentationRequestChannel.ClearTransient:
                    return new PresentationRequest
                    {
                        Kind = PresentationRequestKind.ClearTransientVisualProjection,
                        Owner = _clearTransients[op.Slot],
                    };
                default:
                    throw new InvalidOperationException($"Unknown PresentationRequestChannel '{op.Channel}'.");
            }
        }
    }
}
