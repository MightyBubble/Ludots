using System;
using System.Collections.Generic;

namespace Ludots.Core.Map
{
    public readonly struct MapFeatureFlags : IEquatable<MapFeatureFlags>
    {
        public readonly byte SpatialQueriesEnabled;
        public readonly byte GraphEnabled;
        public readonly byte CameraCullingEnabled;

        public MapFeatureFlags(bool spatialQueriesEnabled, bool graphEnabled, bool cameraCullingEnabled)
        {
            SpatialQueriesEnabled = (byte)(spatialQueriesEnabled ? 1 : 0);
            GraphEnabled = (byte)(graphEnabled ? 1 : 0);
            CameraCullingEnabled = (byte)(cameraCullingEnabled ? 1 : 0);
        }

        public static MapFeatureFlags Defaults => new MapFeatureFlags(spatialQueriesEnabled: true, graphEnabled: true, cameraCullingEnabled: true);

        public static MapFeatureFlags FromTags(IReadOnlyList<string> tags)
        {
            if (tags == null || tags.Count == 0) return Defaults;
            bool spatial = true;
            bool graph = true;
            bool cull = true;

            for (int i = 0; i < tags.Count; i++)
            {
                var t = tags[i];
                if (string.Equals(t, "Feature.SpatialQueries:Off", StringComparison.OrdinalIgnoreCase)) spatial = false;
                else if (string.Equals(t, "Feature.Graph:Off", StringComparison.OrdinalIgnoreCase)) graph = false;
                else if (string.Equals(t, "Feature.CameraCulling:Off", StringComparison.OrdinalIgnoreCase)) cull = false;
            }

            return new MapFeatureFlags(spatial, graph, cull);
        }

        public bool Equals(MapFeatureFlags other)
        {
            return SpatialQueriesEnabled == other.SpatialQueriesEnabled &&
                   GraphEnabled == other.GraphEnabled &&
                   CameraCullingEnabled == other.CameraCullingEnabled;
        }

        public override bool Equals(object obj) => obj is MapFeatureFlags other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(SpatialQueriesEnabled, GraphEnabled, CameraCullingEnabled);
        public static bool operator ==(MapFeatureFlags left, MapFeatureFlags right) => left.Equals(right);
        public static bool operator !=(MapFeatureFlags left, MapFeatureFlags right) => !left.Equals(right);
    }
}

