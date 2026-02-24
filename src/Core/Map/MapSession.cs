using System;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Config;
using Ludots.Core.Navigation.AOI;
using Ludots.Core.Spatial;

namespace Ludots.Core.Map
{
    /// <summary>
    /// Lightweight lifecycle manager for a loaded map.
    /// Tracks the current map identity and provides cleanup when switching maps.
    /// </summary>
    public sealed class MapSession
    {
        public MapId MapId { get; }
        public MapConfig MapConfig { get; }

        private static readonly QueryDescription _mapEntityQuery =
            new QueryDescription().WithAll<MapEntity>();

        public MapSession(MapId mapId, MapConfig mapConfig)
        {
            MapId = mapId;
            MapConfig = mapConfig;
        }

        /// <summary>
        /// Destroy all entities tagged with MapEntity and clear the spatial partition.
        /// Call this before loading a new map.
        /// </summary>
        public void Cleanup(World world, ISpatialPartitionWorld spatialPartition, HexGridAOI hexGridAOI = null)
        {
            if (world == null) return;

            Console.WriteLine($"[MapSession] Cleaning up map '{MapId}'...");

            // Destroy all map-spawned entities
            int destroyed = 0;
            world.Query(in _mapEntityQuery, (Entity entity) =>
            {
                destroyed++;
            });

            // Bulk destroy via query — more efficient than individual Destroy calls
            world.Destroy(in _mapEntityQuery);

            Console.WriteLine($"[MapSession] Destroyed {destroyed} map entities.");

            // Clear spatial partition data
            spatialPartition?.Clear();

            // Reset LoadedChunks — triggers ChunkUnloaded events for all consumers
            hexGridAOI?.Reset();
        }
    }
}
