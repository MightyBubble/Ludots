using Ludots.Core.Config;
using Ludots.Core.Map.Hex;

namespace Ludots.Core.Map
{
    public interface IMapManager
    {
        void RegisterMap(MapDefinition definition);
        MapDefinition GetDefinition<T>() where T : MapDefinition;
        MapConfig LoadMap(string mapId);
        MapConfig LoadMap(MapId mapId);
    }
}