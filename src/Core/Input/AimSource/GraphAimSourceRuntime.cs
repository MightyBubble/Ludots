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
            int kept = 0;
            for (int i = 0; i < count; i++)
            {
                if (SpatialBoundsUtility.EntityIntersectsScreenRect(_world, entities[i], projector, in localRect))
                {
                    entities[kept++] = entities[i];
                }
            }

            return kept;
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
