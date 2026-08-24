using System;
using Arch.Core;
using Ludots.Core.Gameplay.GAS.Components;

namespace Ludots.Core.Gameplay.GAS.Orders
{
    public static class OrderBlackboardStateInstaller
    {
        public const string MissingStateError = "GAS.ORDER_BLACKBOARD.ERR.MissingState";

        public static void EnsureInstalled(World world, Entity entity)
        {
            ArgumentNullException.ThrowIfNull(world);
            if (!world.IsAlive(entity))
            {
                throw new InvalidOperationException($"{MissingStateError}: entity={entity.Id}, state=dead.");
            }

            if (!world.Has<BlackboardIntBuffer>(entity))
            {
                world.Add(entity, new BlackboardIntBuffer());
            }
            if (!world.Has<BlackboardFloatBuffer>(entity))
            {
                world.Add(entity, new BlackboardFloatBuffer());
            }
            if (!world.Has<BlackboardSpatialBuffer>(entity))
            {
                world.Add(entity, new BlackboardSpatialBuffer());
            }
            if (!world.Has<BlackboardEntityBuffer>(entity))
            {
                world.Add(entity, new BlackboardEntityBuffer());
            }
        }

        public static void RequireInstalled(World world, Entity entity)
        {
            ArgumentNullException.ThrowIfNull(world);
            if (!world.IsAlive(entity))
            {
                throw new InvalidOperationException($"{MissingStateError}: entity={entity.Id}, state=dead.");
            }

            bool hasInts = world.Has<BlackboardIntBuffer>(entity);
            bool hasFloats = world.Has<BlackboardFloatBuffer>(entity);
            bool hasSpatial = world.Has<BlackboardSpatialBuffer>(entity);
            bool hasEntities = world.Has<BlackboardEntityBuffer>(entity);
            if (hasInts && hasFloats && hasSpatial && hasEntities)
            {
                return;
            }

            throw new InvalidOperationException(
                $"{MissingStateError}: entity={entity.Id}, " +
                $"BlackboardIntBuffer={hasInts}, BlackboardFloatBuffer={hasFloats}, BlackboardSpatialBuffer={hasSpatial}, BlackboardEntityBuffer={hasEntities}.");
        }
    }
}
