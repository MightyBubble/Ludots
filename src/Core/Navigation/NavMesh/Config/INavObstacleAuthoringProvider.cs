using System.Collections.Generic;
using Ludots.Core.Config;
using Ludots.Core.Gameplay.Spawning;

namespace Ludots.Core.Navigation.NavMesh.Config;

public interface INavObstacleAuthoringProvider
{
    NavObstacleSet BuildForMap(
        MapConfig map,
        IReadOnlyDictionary<string, EntityTemplate> templates,
        string layerId);
}
