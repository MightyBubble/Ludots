using System;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.CrowdSimulation;
using Ludots.Core.Map;
using Ludots.Core.MassNavigation.Runtime;

namespace Ludots.Core.Navigation;

/// <summary>
/// 激活时扫聚焦地图上的实体。一张图的 solver 定了之后，实体不能再带另一种求解器的单位组件。
/// </summary>
internal static class SolverAgentExclusivity
{
    private static readonly QueryDescription MassAgents = new QueryDescription()
        .WithAll<MapEntity, MassNavigationAgent>();

    private static readonly QueryDescription CrowdAgents = new QueryDescription()
        .WithAll<MapEntity, CrowdSimulationAgent>();

    public static void RejectMassNavigationAgent(World world, MapId mapId)
    {
        Entity offender = default;
        bool found = false;
        world.Query(in MassAgents, (Entity entity, ref MapEntity mapEntity, ref MassNavigationAgent _) =>
        {
            if (found || mapEntity.MapId != mapId)
            {
                return;
            }

            offender = entity;
            found = true;
        });
        ThrowIfFound(mapId, offender, found, "MassNavigationAgent");
    }

    public static void RejectCrowdSimulationAgent(World world, MapId mapId)
    {
        Entity offender = default;
        bool found = false;
        world.Query(in CrowdAgents, (Entity entity, ref MapEntity mapEntity, ref CrowdSimulationAgent _) =>
        {
            if (found || mapEntity.MapId != mapId)
            {
                return;
            }

            offender = entity;
            found = true;
        });
        ThrowIfFound(mapId, offender, found, "CrowdSimulationAgent");
    }

    private static void ThrowIfFound(MapId mapId, Entity offender, bool found, string component)
    {
        if (!found)
        {
            return;
        }

        throw new InvalidOperationException(
            $"地图 \"{mapId.Value}\" 的实体 {offender.Id} 带有 {component}。");
    }
}
