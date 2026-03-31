using System;
using Arch.Core;
using Ludots.Core.Gameplay.GAS.Components;

namespace Ludots.Core.Gameplay.GAS
{
    public static class GameplayControlStateResolver
    {
        public static GameplayControlState GetOrDefault(World world, Entity entity)
        {
            if (world != null &&
                entity != Entity.Null &&
                world.IsAlive(entity) &&
                world.TryGet(entity, out GameplayControlState controlState))
            {
                return controlState;
            }

            return GameplayControlState.CreateDefault();
        }

        public static bool IsCastBlocked(World world, Entity entity)
        {
            return GetOrDefault(world, entity).ActionBlocked != 0;
        }

        public static bool IsMoveBlocked(World world, Entity entity)
        {
            return GetOrDefault(world, entity).IsMoveBlocked();
        }

        public static float ResolveMoveSpeed(World world, Entity entity, float baseSpeedCmPerSec)
        {
            if (baseSpeedCmPerSec <= 0f)
            {
                return 0f;
            }

            GameplayControlState controlState = GetOrDefault(world, entity);
            if (controlState.IsMoveBlocked())
            {
                return 0f;
            }
            
            return baseSpeedCmPerSec;
        }
    }
}
