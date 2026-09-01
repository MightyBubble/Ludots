using System;
using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Input.CommandSources;
using Ludots.Core.Input.Runtime;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.Scripting;
using Ludots.Core.Spatial;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Input.AimSource
{
    /// <summary>
    /// Production aimsource kernel over the engine globals: ground resolution reuses the
    /// authoritative pointer helper's camera-ray + heightmap chain, entity pick reuses the
    /// pointer hit resolver's knowledge-gated chain over an explicit candidate set, and the
    /// region filter reuses the spatial bounds utility's screen-rect intersection. The
    /// seat parameter is carried through the contract; until per-seat projector routing
    /// is installed every seat answers under the sole present binding's projector.
    /// </summary>
    public sealed class GraphAimSourceRuntime : IGraphAimSourceRuntime
    {
        private readonly World _world;
        private readonly IReadOnlyDictionary<string, object> _globals;

        public GraphAimSourceRuntime(World world, IReadOnlyDictionary<string, object> globals)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _globals = globals ?? throw new ArgumentNullException(nameof(globals));
        }

        public bool TryScreenPointToGround(float screenX, float screenY, out IntVector2 groundCm)
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

        public Entity PickScreenPointEntity(
            ReadOnlySpan<Entity> candidates,
            int count,
            Entity owner,
            string? seatId,
            float screenX,
            float screenY,
            float radiusPixels)
        {
            if (!TryResolveProjector(out IScreenProjector projector) || count <= 0)
            {
                return Entity.Null;
            }

            return CommandSourcePointerHitResolver.FindNearestInspectableEntity(
                _world,
                AsMutableGlobals(),
                owner,
                candidates.Slice(0, count),
                new Vector2(screenX, screenY),
                radiusPixels,
                projector);
        }

        public int FilterScreenRegionEntities(Span<Entity> entities, int count, in ScreenRect rect)
        {
            if (!TryResolveProjector(out IScreenProjector projector) || count <= 0)
            {
                return 0;
            }

            int kept = 0;
            for (int i = 0; i < count; i++)
            {
                if (SpatialBoundsUtility.EntityIntersectsScreenRect(_world, entities[i], projector, in rect))
                {
                    entities[kept++] = entities[i];
                }
            }

            return kept;
        }

        private bool TryResolveProjector(out IScreenProjector projector)
        {
            if (_globals.TryGetValue(CoreServiceKeys.ScreenProjector.Name, out var projectorObj) &&
                projectorObj is IScreenProjector resolved)
            {
                projector = resolved;
                return true;
            }

            projector = null!;
            return false;
        }

        private Dictionary<string, object> AsMutableGlobals()
        {
            return _globals as Dictionary<string, object>
                ?? throw new InvalidOperationException(
                    "GAS.GRAPH.ERR.AimSourceGlobalsReadOnly: the pointer hit resolver requires a mutable globals store.");
        }
    }
}
