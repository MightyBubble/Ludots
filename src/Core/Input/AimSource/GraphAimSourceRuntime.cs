using System;
using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Client;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Input.CommandSources;
using Ludots.Core.Input.Runtime;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.Presentation.Camera;
using Ludots.Core.Presentation.Terrain;
using Ludots.Core.Presentation.Utils;
using Ludots.Core.Scripting;
using Ludots.Core.Spatial;
using Ludots.Core.EntityCollections;
using Ludots.Core.Components;
using Ludots.Core.Presentation.Components;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Input.AimSource
{
    /// <summary>
    /// Production aimsource kernel over the engine globals: ground resolution reuses the
    /// authoritative pointer helper's camera-ray + heightmap chain, entity pick reuses the
    /// pointer hit resolver's knowledge-gated chain over an explicit candidate set, and the
    /// region filter reuses the spatial bounds utility's screen-rect intersection. A named
    /// seat answers under that seat's present binding (per-binding rebind of the same
    /// CoreScreenRayProvider/CoreScreenProjector the host uses, window points translated
    /// into the binding's local surface); a null seat answers under the installed
    /// sole/global providers, window-rect routing included when several bindings exist.
    /// </summary>
    public sealed class GraphAimSourceRuntime : IGraphAimSourceRuntime
    {
        private readonly World _world;
        private readonly IReadOnlyDictionary<string, object> _globals;

        private CoreScreenRayProvider? _seatRay;
        private CoreScreenProjector? _seatProjector;
        private Entity[] _regionBuffer = new Entity[256];
        private IContinuousHeightmapRenderSource? _heightRangeSource;
        private int _heightRangeRevision = int.MinValue;
        private float _minHeightMeters;
        private float _maxHeightMeters;
        private readonly System.Runtime.CompilerServices.ConditionalWeakTable<IEntityCollectionSource, SourceValidation> _validatedSources = new();
        private sealed class SourceValidation
        {
            public uint Source;
            public long Spatial;
        }
        public long RegionBroadphaseCandidates { get; private set; }
        public long RegionExactTests { get; private set; }

        public ReadOnlySpan<Entity> QueryScreenRegion(IEntityCollectionSource source, in ScreenRect rect, string? seatId)
        {
            source.Read();
            if (!TryResolveProjector(seatId, rect.MinX, rect.MinY, out IScreenProjector projector, out Vector2 origin))
                throw new InvalidOperationException("SPATIAL.ERR.ScreenProjectorMissing");
            IScreenRayProvider rays;
            if (seatId == null)
                rays = _globals.TryGetValue(CoreServiceKeys.ScreenRayProvider.Name, out var rayObject) && rayObject is IScreenRayProvider installed
                    ? installed : throw new InvalidOperationException("SPATIAL.ERR.ScreenRayProviderMissing");
            else
            {
                if (!TryBindSeatSurface(seatId, out PresentBinding binding, out IViewController view))
                    throw new InvalidOperationException("SPATIAL.ERR.SeatSurfaceMissing");
                CameraManager camera = ClientLocalSeatAccess.RequireLogicViews(_globals).RequireCamera(binding.LogicViewId);
                _seatRay ??= new CoreScreenRayProvider(camera, view);
                _seatRay.Rebind(camera, new PresentBindingSurface(binding, view.Fov));
                rays = _seatRay;
            }
            var localRect = new ScreenRect(origin.X, origin.Y, origin.X + rect.MaxX - rect.MinX, origin.Y + rect.MaxY - rect.MinY);
            ScreenProjectionPoseContext projectionPose = ScreenProjectionGrounding.Resolve(_world, _globals);
            var spatial = _globals[CoreServiceKeys.SpatialQueryService.Name] as SpatialQueryService
                ?? throw new InvalidOperationException("SPATIAL.ERR.SharedQueryServiceMissing");
            SourceValidation validated = _validatedSources.GetValue(source, static _ => new SourceValidation());
            if (validated.Source != source.Revision || validated.Spatial != spatial.MembershipRevision)
            {
                foreach (Entity entity in source.Read())
                {
                    if (!_world.TryGet(entity, out SpatialCellRef membership) || membership.State != SpatialMembershipState.Active ||
                        _world.Has<SuspendedTag>(entity) || _world.Has<PresentationStaticTransform>(entity) ||
                        _world.Has<SpatialPartitionExcluded>(entity) || _world.Has<PresentationDestroyPending>(entity))
                        throw new InvalidOperationException($"SPATIAL.ERR.CandidateNotIndexed: entity={entity.Id}");
                }
                validated.Source = source.Revision;
                validated.Spatial = spatial.MembershipRevision;
            }
            var spec = (WorldSizeSpec)_globals[CoreServiceKeys.WorldSizeSpec.Name];
            (float minHeightMeters, float maxHeightMeters) = ResolveHeightSpan(projectionPose.GroundHeightmap);
            if (!ScreenRegionBroadphase.TryGetBounds(rays, localRect, spec.Bounds, spatial.ReadBoundsRadiusCm(), out WorldAabbCm bounds, minHeightMeters, maxHeightMeters))
                return ReadOnlySpan<Entity>.Empty;
            int halfWidth = checked((bounds.Width + 1) / 2);
            int halfHeight = checked((bounds.Height + 1) / 2);
            var center = new WorldCmInt2(checked(bounds.Left + halfWidth), checked(bounds.Top + halfHeight));
            SpatialQueryResult result;
            do
            {
                result = spatial.QueryRectangle(center, halfWidth, halfHeight, 0, _regionBuffer);
                if (result.Dropped > 0) Array.Resize(ref _regionBuffer, checked(_regionBuffer.Length + result.Dropped));
            } while (result.Dropped > 0);
            RegionBroadphaseCandidates += result.Count;
            int kept = 0;
            for (int i = 0; i < result.Count; i++)
            {
                Entity entity = _regionBuffer[i];
                if (!source.Contains(entity)) continue;
                RegionExactTests++;
                if (SpatialBoundsUtility.EntityIntersectsScreenRect(_world, entity, projector, localRect, in projectionPose))
                    _regionBuffer[kept++] = entity;
            }
            return _regionBuffer.AsSpan(0, kept);
        }


        private (float Min, float Max) ResolveHeightSpan(IContinuousHeightmap? heightmap)
        {
            if (heightmap == null)
            {
                return (0f, 0f);
            }

            if (heightmap is not IContinuousHeightmapRenderSource source)
            {
                throw new InvalidOperationException("SPATIAL.ERR.HeightRangeUnavailable");
            }

            if (ReferenceEquals(source, _heightRangeSource) && source.Revision == _heightRangeRevision)
            {
                return (_minHeightMeters, _maxHeightMeters);
            }

            float minCm = float.MaxValue;
            float maxCm = float.MinValue;
            int sampleCount = 0;
            for (int chunkY = 0; chunkY < source.ChunkRows; chunkY++)
            {
                for (int chunkX = 0; chunkX < source.ChunkColumns; chunkX++)
                {
                    if (!source.TryGetChunk(chunkX, chunkY, out ContinuousHeightmapRenderChunk chunk))
                    {
                        continue;
                    }

                    for (int sampleY = 0; sampleY < chunk.SampleRows; sampleY++)
                    {
                        for (int sampleX = 0; sampleX < chunk.SampleColumns; sampleX++)
                        {
                            if (!chunk.TryReadHeightCm(sampleX, sampleY, out float heightCm) || !float.IsFinite(heightCm))
                            {
                                throw new InvalidOperationException(
                                    $"SPATIAL.ERR.InvalidHeightSample: chunk=({chunkX},{chunkY}), sample=({sampleX},{sampleY})");
                            }

                            minCm = MathF.Min(minCm, heightCm);
                            maxCm = MathF.Max(maxCm, heightCm);
                            sampleCount++;
                        }
                    }
                }
            }

            if (sampleCount == 0)
            {
                throw new InvalidOperationException("SPATIAL.ERR.HeightRangeEmpty");
            }

            _heightRangeSource = source;
            _heightRangeRevision = source.Revision;
            _minHeightMeters = minCm / 100f;
            _maxHeightMeters = maxCm / 100f;
            return (_minHeightMeters, _maxHeightMeters);
        }

        public GraphAimSourceRuntime(World world, IReadOnlyDictionary<string, object> globals)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _globals = globals ?? throw new ArgumentNullException(nameof(globals));
        }

        public bool TryScreenPointToGround(float screenX, float screenY, string? seatId, out IntVector2 groundCm)
        {
            if (seatId == null)
            {
                if (!AuthoritativeGroundPointerHelper.TryResolveFromScreen(
                        _globals,
                        new Vector2(screenX, screenY),
                        out WorldCmInt2 worldCm))
                {
                    groundCm = default;
                    return false;
                }

                groundCm = new IntVector2(worldCm.X, worldCm.Y);
                return true;
            }

            if (!TryBindSeatSurface(seatId, out PresentBinding binding, out IViewController hostView) ||
                !_globals.TryGetValue(CoreServiceKeys.ContinuousHeightmap.Name, out var heightmapObj) ||
                heightmapObj is not IContinuousHeightmap heightmap ||
                !_globals.TryGetValue(CoreServiceKeys.WorldSizeSpec.Name, out var worldSizeObj) ||
                worldSizeObj is not WorldSizeSpec worldSize)
            {
                groundCm = default;
                return false;
            }

            try
            {
                CameraManager camera = ClientLocalSeatAccess.RequireLogicViews(_globals).RequireCamera(binding.LogicViewId);
                CoreScreenRayProvider ray = _seatRay ??= new CoreScreenRayProvider(camera, hostView);
                ray.Rebind(camera, new PresentBindingSurface(binding, hostView.Fov));
                ScreenRay screenRay = ray.GetRay(ToBindingLocal(binding, hostView, screenX, screenY));
                if (!GroundRaycastUtil.TryGetGroundWorldCmBounded(in screenRay, heightmap, worldSize, out WorldCmInt2 worldCm))
                {
                    groundCm = default;
                    return false;
                }

                groundCm = new IntVector2(worldCm.X, worldCm.Y);
                return true;
            }
            catch (ArgumentOutOfRangeException)
            {
                groundCm = default;
                return false;
            }
        }

        public Entity PickScreenPointEntity(
            ReadOnlySpan<Entity> candidates,
            int count,
            Entity owner,
            string? seatId,
            float screenX,
            float screenY,
            float radiusPixels)
        {
            if (!TryResolveProjector(seatId, screenX, screenY, out IScreenProjector projector, out Vector2 localPoint) ||
                count <= 0)
            {
                return Entity.Null;
            }

            return CommandSourcePointerHitResolver.FindNearestInspectableEntity(
                _world,
                AsMutableGlobals(),
                owner,
                candidates.Slice(0, count),
                localPoint,
                radiusPixels,
                projector);
        }

        public int FilterScreenRegionEntities(Span<Entity> entities, int count, in ScreenRect rect, string? seatId)
        {
            if (!TryResolveProjector(seatId, rect.MinX, rect.MinY, out IScreenProjector projector, out Vector2 localOrigin) ||
                count <= 0)
            {
                return 0;
            }

            var localRect = new ScreenRect(
                localOrigin.X,
                localOrigin.Y,
                localOrigin.X + (rect.MaxX - rect.MinX),
                localOrigin.Y + (rect.MaxY - rect.MinY));
            ScreenProjectionPoseContext projectionPose = ScreenProjectionGrounding.Resolve(_world, _globals);
            int kept = 0;
            for (int i = 0; i < count; i++)
            {
                if (SpatialBoundsUtility.EntityIntersectsScreenRect(_world, entities[i], projector, in localRect, in projectionPose))
                {
                    entities[kept++] = entities[i];
                }
            }

            return kept;
        }

        public bool TryReadLivePointerScreen(out float screenX, out float screenY)
        {
            if (!Ludots.Core.Input.Interaction.PointerInteractionSnapshotReader.TryRead(_globals, out var snapshot))
            {
                screenX = 0f;
                screenY = 0f;
                return false;
            }

            screenX = snapshot.Pointer.X;
            screenY = snapshot.Pointer.Y;
            return float.IsFinite(screenX) && float.IsFinite(screenY);
        }

        private bool TryResolveProjector(string? seatId, float screenX, float screenY, out IScreenProjector projector, out Vector2 bindingLocalPoint)
        {
            if (seatId == null)
            {
                if (_globals.TryGetValue(CoreServiceKeys.ScreenProjector.Name, out var projectorObj) &&
                    projectorObj is IScreenProjector resolved)
                {
                    projector = resolved;
                    bindingLocalPoint = new Vector2(screenX, screenY);
                    return true;
                }

                projector = null!;
                bindingLocalPoint = default;
                return false;
            }

            if (!TryBindSeatSurface(seatId, out PresentBinding binding, out IViewController hostView))
            {
                projector = null!;
                bindingLocalPoint = default;
                return false;
            }

            CameraManager camera = ClientLocalSeatAccess.RequireLogicViews(_globals).RequireCamera(binding.LogicViewId);
            _seatProjector ??= new CoreScreenProjector(camera, hostView);
            _seatProjector.Rebind(camera, new PresentBindingSurface(binding, hostView.Fov));
            projector = _seatProjector;
            bindingLocalPoint = ToBindingLocal(binding, hostView, screenX, screenY);
            return true;
        }

        private bool TryBindSeatSurface(string seatId, out PresentBinding binding, out IViewController hostView)
        {
            binding = default;
            if (!_globals.TryGetValue(CoreServiceKeys.ClientLocalSeatRegistry.Name, out var seatsObj) ||
                seatsObj is not ClientLocalSeatRegistry seats ||
                !seats.TryGet(seatId, out ClientLocalSeat seat) ||
                seat.PresentBinding is not PresentBinding present)
            {
                hostView = null!;
                return false;
            }

            if (!_globals.TryGetValue(CoreServiceKeys.ViewController.Name, out var viewObj) ||
                viewObj is not IViewController view ||
                view.Resolution.X <= 0f ||
                view.Resolution.Y <= 0f)
            {
                hostView = null!;
                return false;
            }

            binding = present;
            hostView = view;
            return true;
        }

        private static Vector2 ToBindingLocal(PresentBinding binding, IViewController hostView, float windowX, float windowY)
        {
            Vector2 hostResolution = hostView.Resolution;
            Vector4 rect = binding.NormalizedScreenRect;
            return new Vector2(
                windowX - (rect.X * hostResolution.X),
                windowY - (rect.Y * hostResolution.Y));
        }

        private Dictionary<string, object> AsMutableGlobals()
        {
            return _globals as Dictionary<string, object>
                ?? throw new InvalidOperationException(
                    "GAS.GRAPH.ERR.AimSourceGlobalsReadOnly: the pointer hit resolver requires a mutable globals store.");
        }
    }
}
