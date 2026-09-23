using Arch.Core;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;

namespace Ludots.Core.MassNavigation.Runtime;

public static class MassNavigationBlackboardKeys
{
    public const string AgentLocomotionSpeedKey = "mass_navigation.agent.locomotion.speed";

    public static int AgentLocomotionSpeed => ConfigKeyRegistry.Register(AgentLocomotionSpeedKey);
}

internal static class MassNavigationBlackboardWriter
{
    public static void SetAgentLocomotionSpeed(World world, Entity entity, float speed)
    {
        if (world.Has<BlackboardFloatBuffer>(entity))
        {
            ref BlackboardFloatBuffer buffer = ref world.Get<BlackboardFloatBuffer>(entity);
            SetAgentLocomotionSpeed(ref buffer, speed);
            return;
        }

        var bufferToAdd = new BlackboardFloatBuffer();
        SetAgentLocomotionSpeed(ref bufferToAdd, speed);
        world.Add(entity, bufferToAdd);
    }

    public static void SetAgentLocomotionSpeed(ref BlackboardFloatBuffer buffer, float speed)
    {
        buffer.Set(MassNavigationBlackboardKeys.AgentLocomotionSpeed, speed);
    }
}
