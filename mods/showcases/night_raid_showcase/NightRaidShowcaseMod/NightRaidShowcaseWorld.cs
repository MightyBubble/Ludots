using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;

namespace NightRaidShowcaseMod;

internal static class NightRaidShowcaseWorld
{
    public const string MapId = "night_raid";
    public const string HeroName = "NightRaidHero";
    public const string ActActionId = "NightRaid.Act";
    public const float StrikeDamage = 20f;
    public const string MoveActionId = "NightRaid.Move";
    public const float MoveSpeedCmPerSecond = 320f;
    public const int ActThrottleTicks = 2;
    public const float RaidCircleRadiusCm = 250f;

    public static Entity FindHero(World world)
    {
        Entity hero = Entity.Null;
        var query = new QueryDescription().WithAll<Name>();
        world.Query(in query, (Entity entity, ref Name name) =>
        {
            if (hero == Entity.Null && string.Equals(name.Value, HeroName, StringComparison.Ordinal))
            {
                hero = entity;
            }
        });
        return hero;
    }

    public static Entity FindNearestHostile(World world, Entity from)
    {
        if (from == Entity.Null || !world.IsAlive(from) || !world.TryGet(from, out WorldPositionCm origin))
        {
            return Entity.Null;
        }

        int healthId = AttributeRegistry.GetId("Health");
        Entity nearest = Entity.Null;
        float nearestDistanceSq = float.MaxValue;
        float ox = origin.Value.X.ToFloat();
        float oy = origin.Value.Y.ToFloat();
        var query = new QueryDescription().WithAll<Team, WorldPositionCm>();
        world.Query(in query, (Entity entity, ref Team team, ref WorldPositionCm position) =>
        {
            if (entity == from || !world.IsAlive(entity) || team.Id is < 2 or > 4)
            {
                return;
            }

            if (healthId >= 0 && world.TryGet(entity, out AttributeBuffer attributes) && attributes.GetCurrent(healthId) <= 0f)
            {
                return;
            }

            float dx = position.Value.X.ToFloat() - ox;
            float dy = position.Value.Y.ToFloat() - oy;
            float distanceSq = dx * dx + dy * dy;
            if (distanceSq < nearestDistanceSq)
            {
                nearestDistanceSq = distanceSq;
                nearest = entity;
            }
        });
        return nearest;
    }
}
