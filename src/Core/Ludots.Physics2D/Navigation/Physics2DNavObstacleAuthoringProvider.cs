using System.Collections.Generic;
using Ludots.Core.Config;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Navigation.NavMesh.Config;

namespace Ludots.Core.Physics2D.Navigation;

public sealed class Physics2DNavObstacleAuthoringProvider : INavObstacleAuthoringProvider
{
    public NavObstacleSet BuildForMap(
        MapConfig map,
        IReadOnlyDictionary<string, EntityTemplate> templates,
        string layerId)
    {
        return NavObstacleAuthoringAdapter.BuildFromMapAuthoring(map, templates, layerId);
    }
}
