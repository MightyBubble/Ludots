using System;
using Arch.Core;
using Ludots.Core.Presentation.Rendering;

namespace Ludots.Core.Presentation.Requests
{
    public sealed class PresentationRequestBuffer
    {
        private readonly VisualProxyChannelItem[] _visualProxies;
        private readonly PrefabRequest[] _prefabs;
        private readonly GroundOverlayChannelItem[] _groundOverlays;
        private readonly WorldHudChannelItem[] _worldHud;
        private readonly SplineRibbonChannelItem[] _splineRibbons;
        private readonly SurfaceSourceChannelItem[] _surfaceSources;
        private readonly PresentationRemovalRequest[] _removals;
        private readonly Entity[] _clearTransients;
        // Mixed-kind enqueue order must be preserved; flushing by channel would invert same-frame remove-then-add.
        private readonly PresentationRequestOp[] _ops;
        private PresentationRequest[]? _spanScratch;
        private int _visualProxyCount;
        private int _prefabCount;
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
            _prefabs = new PrefabRequest[capacities.Prefab];
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

        internal int PrefabCount => _prefabCount;

        internal int ClearTransientCount => _clearTransientCount;

        internal ReadOnlySpan<VisualProxyChannelItem> VisualProxies => _visualProxies.AsSpan(0, _visualProxyCount);

        public void Add(in PresentationRequest request)
        {
            switch (request.Kind)
            {
                case PresentationRequestKind.VisualProxy:
                    AddVisualProxy(in request);
                    break;
                case PresentationRequestKind.Prefab:
                    AddPrefab(in request);
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

            EnsureSpanScratch();
            for (int i = 0; i < _opCount; i++)
            {
                _spanScratch![i] = Reconstruct(i);
            }

            return _spanScratch.AsSpan(0, _opCount);
        }

        public ref readonly PresentationRequest Get(int index)
        {
            if ((uint)index >= (uint)_opCount)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            EnsureSpanScratch();
            _spanScratch![index] = Reconstruct(index);
            return ref _spanScratch[index];
        }

        public void Clear()
        {
            _visualProxyCount = 0;
            _prefabCount = 0;
            _groundOverlayCount = 0;
            _worldHudCount = 0;
            _splineRibbonCount = 0;
            _surfaceSourceCount = 0;
            _removalCount = 0;
            _clearTransientCount = 0;
            _opCount = 0;
        }

        internal ref readonly PrefabRequest PrefabAt(int slot) => ref _prefabs[slot];

        internal ref readonly GroundOverlayChannelItem GroundOverlayAt(int slot) => ref _groundOverlays[slot];

        internal ref readonly WorldHudChannelItem WorldHudAt(int slot) => ref _worldHud[slot];

        internal ref readonly SplineRibbonChannelItem SplineRibbonAt(int slot) => ref _splineRibbons[slot];

        internal ref readonly SurfaceSourceChannelItem SurfaceSourceAt(int slot) => ref _surfaceSources[slot];

        internal ref readonly PresentationRemovalRequest RemovalAt(int slot) => ref _removals[slot];

        internal ref readonly VisualProxyChannelItem VisualProxyAt(int slot) => ref _visualProxies[slot];

        private void AddVisualProxy(in PresentationRequest request)
        {
            EnsureChannelRoom(_visualProxyCount, _visualProxies.Length, request.Kind, request.VisualProxy.StableId);
            int slot = _visualProxyCount++;
            _visualProxies[slot] = new VisualProxyChannelItem
            {
                Owner = request.Owner,
                VisualProxy = request.VisualProxy,
            };
            RecordOp(PresentationRequestChannel.VisualProxy, slot, request.Kind, request.VisualProxy.StableId);
        }

        private void AddPrefab(in PresentationRequest request)
        {
            EnsureChannelRoom(_prefabCount, _prefabs.Length, request.Kind, request.StableId);
            int slot = _prefabCount++;
            _prefabs[slot] = new PrefabRequest
            {
                Owner = request.Owner,
                PrefabId = request.PrefabId,
                StableId = request.StableId,
                Position = request.Position,
                Rotation = request.Rotation,
                Scale = request.Scale,
                Color = request.Color,
                LOD = request.LOD,
            };
            RecordOp(PresentationRequestChannel.Prefab, slot, request.Kind, request.StableId);
        }

        private void AddGroundOverlay(in PresentationRequest request)
        {
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

        private void AddRemoval(in PresentationRequest request)
        {
            EnsureChannelRoom(_removalCount, _removals.Length, request.Kind, request.StableId);
            int slot = _removalCount++;
            _removals[slot] = new PresentationRemovalRequest
            {
                Kind = request.Kind,
                Owner = request.Owner,
                StableId = request.StableId,
            };
            RecordOp(PresentationRequestChannel.Removal, slot, request.Kind, request.StableId);
        }

        private void AddClearTransient(in PresentationRequest request)
        {
            EnsureChannelRoom(_clearTransientCount, _clearTransients.Length, request.Kind, request.StableId);
            int slot = _clearTransientCount++;
            _clearTransients[slot] = request.Owner;
            RecordOp(PresentationRequestChannel.ClearTransient, slot, request.Kind, request.StableId);
        }

        private void RecordOp(PresentationRequestChannel channel, int slot, PresentationRequestKind kind, int stableId)
        {
            if (_opCount >= _ops.Length)
            {
                throw new InvalidOperationException(
                    $"PresentationRequestBuffer overflowed while adding kind={kind}, stableId={stableId}.");
            }

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

        private void EnsureSpanScratch()
        {
            if (_spanScratch != null && _spanScratch.Length >= _opCount)
            {
                return;
            }

            _spanScratch = new PresentationRequest[_ops.Length];
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
                case PresentationRequestChannel.Prefab:
                {
                    ref readonly PrefabRequest item = ref _prefabs[op.Slot];
                    return new PresentationRequest
                    {
                        Kind = PresentationRequestKind.Prefab,
                        Owner = item.Owner,
                        PrefabId = item.PrefabId,
                        StableId = item.StableId,
                        Position = item.Position,
                        Rotation = item.Rotation,
                        Scale = item.Scale,
                        Color = item.Color,
                        LOD = item.LOD,
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
