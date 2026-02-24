using Ludots.Core.Map;

namespace Ludots.Core.Components
{
    /// <summary>
    /// Marker component attached to entities spawned by MapLoader.
    /// Enables bulk cleanup when switching maps via MapSession.Cleanup().
    /// </summary>
    public struct MapEntity
    {
        public MapId MapId;
    }
}
