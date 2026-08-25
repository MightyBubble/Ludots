using System;
using Arch.Core;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Rendering;
using Ludots.Platform.Abstractions;

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
                    AddVisualProxy(request.Owner, in request.VisualProxy);
                    break;
                case PresentationRequestKind.GroundOverlay:
                    AddGroundOverlay(request.Owner, in request.GroundOverlay, request.LOD);
                    break;
                case PresentationRequestKind.WorldHud:
                    AddWorldHud(request.Owner, in request.WorldHud, request.LOD);
                    break;
                case PresentationRequestKind.SplineRibbon:
                    AddSplineRibbon(request.Owner, in request.SplineRibbon, request.LOD);
                    break;
                case PresentationRequestKind.SurfaceSource:
                    AddSurfaceSource(request.Owner, in request.SurfaceSource, request.LOD);
                    break;
                case PresentationRequestKind.RemoveGroundOverlay:
                case PresentationRequestKind.RemoveWorldHud:
                case PresentationRequestKind.RemoveSplineRibbon:
                case PresentationRequestKind.RemoveSurfaceSource:
                    AddRemoval(request.Owner, request.Kind, request.StableId);
                    break;
                case PresentationRequestKind.ClearTransientVisualProjection:
                    AddClearTransient(request.Owner);
                    break;
                default:
                    throw new InvalidOperationException($"Unknown PresentationRequestKind '{request.Kind}'.");
            }
        }

        public void AddVisualProxy(Entity owner, in PresentationVisualProxy proxy)
        {
            EnsureOperationRoom(PresentationRequestKind.VisualProxy, proxy.StableId);
            EnsureChannelRoom(_visualProxyCount, _visualProxies.Length, PresentationRequestKind.VisualProxy, proxy.StableId);
            int slot = _visualProxyCount++;
            _visualProxies[slot] = new VisualProxyChannelItem
            {
                Owner = owner,
                VisualProxy = proxy,
            };
            RecordOp(PresentationRequestChannel.VisualProxy, slot);
        }

        public void AddGroundOverlay(Entity owner, in GroundOverlayItem item, LODLevel lod)
        {
            EnsureOperationRoom(PresentationRequestKind.GroundOverlay, item.StableId);
            EnsureChannelRoom(_groundOverlayCount, _groundOverlays.Length, PresentationRequestKind.GroundOverlay, item.StableId);
            int slot = _groundOverlayCount++;
            _groundOverlays[slot] = new GroundOverlayChannelItem
            {
                Owner = owner,
                LOD = lod,
                Item = item,
            };
            RecordOp(PresentationRequestChannel.GroundOverlay, slot);
        }

        public void AddWorldHud(Entity owner, in WorldHudItem item, LODLevel lod)
        {
            WorldHudItem ownedItem = item;
            ownedItem.Owner = owner;
            EnsureOperationRoom(PresentationRequestKind.WorldHud, ownedItem.StableId);
            EnsureChannelRoom(_worldHudCount, _worldHud.Length, PresentationRequestKind.WorldHud, ownedItem.StableId);
            int slot = _worldHudCount++;
            _worldHud[slot] = new WorldHudChannelItem
            {
                Owner = owner,
                LOD = lod,
                Item = ownedItem,
            };
            RecordOp(PresentationRequestChannel.WorldHud, slot);
        }

        public void AddSplineRibbon(Entity owner, in SplineRibbonRequest spline, LODLevel lod)
        {
            EnsureOperationRoom(PresentationRequestKind.SplineRibbon, spline.StableId);
            EnsureChannelRoom(_splineRibbonCount, _splineRibbons.Length, PresentationRequestKind.SplineRibbon, spline.StableId);
            int slot = _splineRibbonCount++;
            _splineRibbons[slot] = new SplineRibbonChannelItem
            {
                Owner = owner,
                LOD = lod,
                Item = spline,
            };
            RecordOp(PresentationRequestChannel.SplineRibbon, slot);
        }

        public void AddSurfaceSource(Entity owner, in SurfaceSourceRequest surfaceSource, LODLevel lod)
        {
            EnsureOperationRoom(PresentationRequestKind.SurfaceSource, surfaceSource.StableId);
            EnsureChannelRoom(_surfaceSourceCount, _surfaceSources.Length, PresentationRequestKind.SurfaceSource, surfaceSource.StableId);
            int slot = _surfaceSourceCount++;
            _surfaceSources[slot] = new SurfaceSourceChannelItem
            {
                Owner = owner,
                LOD = lod,
                Item = surfaceSource,
            };
            RecordOp(PresentationRequestChannel.SurfaceSource, slot);
        }

        public void RemoveGroundOverlay(Entity owner, int stableId) => AddRemoval(owner, PresentationRequestKind.RemoveGroundOverlay, stableId);

        public void RemoveWorldHud(Entity owner, int stableId) => AddRemoval(owner, PresentationRequestKind.RemoveWorldHud, stableId);

        public void RemoveSplineRibbon(Entity owner, int stableId) => AddRemoval(owner, PresentationRequestKind.RemoveSplineRibbon, stableId);

        public void RemoveSurfaceSource(Entity owner, int stableId) => AddRemoval(owner, PresentationRequestKind.RemoveSurfaceSource, stableId);

        public void ClearTransientVisualProjection(Entity owner) => AddClearTransient(owner);

        public PresentationRequestReplay CaptureReplay(int index)
        {
            if ((uint)index >= (uint)_opCount)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            PresentationRequestOp op = _ops[index];
            switch (op.Channel)
            {
                case PresentationRequestChannel.VisualProxy:
                {
                    ref readonly VisualProxyChannelItem item = ref _visualProxies[op.Slot];
                    return new PresentationRequestReplay
                    {
                        Kind = PresentationRequestKind.VisualProxy,
                        Owner = item.Owner,
                        LOD = item.VisualProxy.LOD,
                        StableId = item.VisualProxy.StableId,
                        VisualProxy = item.VisualProxy,
                    };
                }
                case PresentationRequestChannel.GroundOverlay:
                {
                    ref readonly GroundOverlayChannelItem item = ref _groundOverlays[op.Slot];
                    return new PresentationRequestReplay
                    {
                        Kind = PresentationRequestKind.GroundOverlay,
                        Owner = item.Owner,
                        LOD = item.LOD,
                        StableId = item.Item.StableId,
                        GroundOverlay = item.Item,
                    };
                }
                case PresentationRequestChannel.WorldHud:
                {
                    ref readonly WorldHudChannelItem item = ref _worldHud[op.Slot];
                    return new PresentationRequestReplay
                    {
                        Kind = PresentationRequestKind.WorldHud,
                        Owner = item.Owner,
                        LOD = item.LOD,
                        StableId = item.Item.StableId,
                        WorldHud = item.Item,
                    };
                }
                case PresentationRequestChannel.SplineRibbon:
                {
                    ref readonly SplineRibbonChannelItem item = ref _splineRibbons[op.Slot];
                    return new PresentationRequestReplay
                    {
                        Kind = PresentationRequestKind.SplineRibbon,
                        Owner = item.Owner,
                        StableId = item.Item.StableId,
                        LOD = item.LOD,
                        SplineRibbon = item.Item,
                    };
                }
                case PresentationRequestChannel.SurfaceSource:
                {
                    ref readonly SurfaceSourceChannelItem item = ref _surfaceSources[op.Slot];
                    return new PresentationRequestReplay
                    {
                        Kind = PresentationRequestKind.SurfaceSource,
                        Owner = item.Owner,
                        StableId = item.Item.StableId,
                        LOD = item.LOD,
                        SurfaceSource = item.Item,
                    };
                }
                case PresentationRequestChannel.Removal:
                {
                    ref readonly PresentationRemovalRequest item = ref _removals[op.Slot];
                    return new PresentationRequestReplay
                    {
                        Kind = item.Kind,
                        Owner = item.Owner,
                        StableId = item.StableId,
                    };
                }
                case PresentationRequestChannel.ClearTransient:
                    return new PresentationRequestReplay
                    {
                        Kind = PresentationRequestKind.ClearTransientVisualProjection,
                        Owner = _clearTransients[op.Slot],
                    };
                default:
                    throw new InvalidOperationException($"Unknown PresentationRequestChannel '{op.Channel}'.");
            }
        }

        public void Replay(in PresentationRequestReplay replay)
        {
            switch (replay.Kind)
            {
                case PresentationRequestKind.VisualProxy:
                    AddVisualProxy(replay.Owner, in replay.VisualProxy);
                    break;
                case PresentationRequestKind.GroundOverlay:
                    AddGroundOverlay(replay.Owner, in replay.GroundOverlay, replay.LOD);
                    break;
                case PresentationRequestKind.WorldHud:
                    AddWorldHud(replay.Owner, in replay.WorldHud, replay.LOD);
                    break;
                case PresentationRequestKind.SplineRibbon:
                    AddSplineRibbon(replay.Owner, in replay.SplineRibbon, replay.LOD);
                    break;
                case PresentationRequestKind.SurfaceSource:
                    AddSurfaceSource(replay.Owner, in replay.SurfaceSource, replay.LOD);
                    break;
                case PresentationRequestKind.RemoveGroundOverlay:
                case PresentationRequestKind.RemoveWorldHud:
                case PresentationRequestKind.RemoveSplineRibbon:
                case PresentationRequestKind.RemoveSurfaceSource:
                    AddRemoval(replay.Owner, replay.Kind, replay.StableId);
                    break;
                case PresentationRequestKind.ClearTransientVisualProjection:
                    AddClearTransient(replay.Owner);
                    break;
                default:
                    throw new InvalidOperationException($"Unknown PresentationRequestKind '{replay.Kind}'.");
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
            RecordOp(PresentationRequestChannel.Removal, slot);
        }

        private void AddClearTransient(Entity owner)
        {
            EnsureOperationRoom(PresentationRequestKind.ClearTransientVisualProjection, 0);
            EnsureChannelRoom(_clearTransientCount, _clearTransients.Length, PresentationRequestKind.ClearTransientVisualProjection, 0);
            int slot = _clearTransientCount++;
            _clearTransients[slot] = owner;
            RecordOp(PresentationRequestChannel.ClearTransient, slot);
        }

        private void EnsureOperationRoom(PresentationRequestKind kind, int stableId)
        {
            if (_opCount >= _ops.Length)
            {
                throw new InvalidOperationException(
                    $"PresentationRequestBuffer overflowed while adding kind={kind}, stableId={stableId}.");
            }
        }

        private void RecordOp(PresentationRequestChannel channel, int slot)
        {
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
