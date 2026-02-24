using System;
using System.Runtime.CompilerServices;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Spatial;

namespace Ludots.Core.Systems
{
    public sealed class WorldToGridSyncSystem : BaseSystem<World, float>
    {
        private ISpatialCoordinateConverter _coords;
        private readonly QueryDescription _query = new QueryDescription()
            .WithAll<WorldPositionCm, Position>();

        public WorldToGridSyncSystem(World world, ISpatialCoordinateConverter coords) : base(world)
        {
            _coords = coords;
        }

        /// <summary>
        /// Hot-swap the coordinate converter when the spatial config changes (e.g. on map load).
        /// Called by GameEngine.ApplyMapSpatialConfig to prevent stale references.
        /// </summary>
        internal void SetCoordinateConverter(ISpatialCoordinateConverter coords)
        {
            _coords = coords ?? throw new ArgumentNullException(nameof(coords));
        }

        public override void Update(in float dt)
        {
            var job = new SyncJob { Coords = _coords };
            World.InlineEntityQuery<SyncJob, WorldPositionCm, Position>(in _query, ref job);
        }

        struct SyncJob : IForEachWithEntity<WorldPositionCm, Position>
        {
            public ISpatialCoordinateConverter Coords;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Update(Entity entity, ref WorldPositionCm worldPos, ref Position gridPos)
            {
                gridPos.GridPos = Coords.WorldToGrid(worldPos.Value.ToWorldCmInt2());
            }
        }
    }
}
