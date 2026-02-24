using Arch.Core;
using Arch.Buffer;
using Ludots.Core.Gameplay.GAS.Components;

namespace Ludots.Core.Gameplay.GAS
{
    public static class GameplayEffectFactory
    {
        private static readonly ComponentType[] GameplayEffectArchetype = [typeof(GameplayEffect), typeof(EffectContext), typeof(EffectModifiers), typeof(EffectResolveOrder)];

        public readonly struct EffectCreateCommand
        {
            public readonly Entity Source;
            public readonly Entity Target;
            public readonly Entity TargetContext;
            public readonly int DurationTicks;
            public readonly int PeriodTicks;
            public readonly EffectLifetimeKind LifetimeKind;
            public readonly GasClockId ClockId;
            public readonly GasConditionHandle ExpireCondition;

            public EffectCreateCommand(Entity source, Entity target, int durationTicks, EffectLifetimeKind lifetimeKind, int periodTicks = 0, GasClockId clockId = GasClockId.FixedFrame, GasConditionHandle expireCondition = default)
            {
                Source = source;
                Target = target;
                TargetContext = default;
                DurationTicks = durationTicks;
                PeriodTicks = periodTicks;
                LifetimeKind = lifetimeKind;
                ClockId = clockId;
                ExpireCondition = expireCondition;
            }

            public EffectCreateCommand(Entity source, Entity target, Entity targetContext, int durationTicks, EffectLifetimeKind lifetimeKind, int periodTicks = 0, GasClockId clockId = GasClockId.FixedFrame, GasConditionHandle expireCondition = default)
            {
                Source = source;
                Target = target;
                TargetContext = targetContext;
                DurationTicks = durationTicks;
                PeriodTicks = periodTicks;
                LifetimeKind = lifetimeKind;
                ClockId = clockId;
                ExpireCondition = expireCondition;
            }
        }

        public static Entity CreateEffect(World world, Entity source, Entity target, int durationTicks, EffectLifetimeKind lifetimeKind, int periodTicks = 0, Entity targetContext = default, GasClockId clockId = GasClockId.FixedFrame, GasConditionHandle expireCondition = default)
        {
            return CreateEffect(world, rootId: 0, source, target, durationTicks, lifetimeKind, periodTicks, targetContext, clockId, expireCondition);
        }

        public static Entity CreateEffect(World world, int rootId, Entity source, Entity target, int durationTicks, EffectLifetimeKind lifetimeKind, int periodTicks = 0, Entity targetContext = default, GasClockId clockId = GasClockId.FixedFrame, GasConditionHandle expireCondition = default)
        {
            var ge = new GameplayEffect 
            { 
                LifetimeKind = lifetimeKind,
                ClockId = clockId,
                TotalTicks = durationTicks,
                RemainingTicks = durationTicks,
                PeriodTicks = periodTicks,
                NextTickAtTick = 0,
                ExpiresAtTick = 0,
                ExpireCondition = expireCondition
            };
            ge.State = EffectState.Pending;
            var entity = world.Create(
                ge,
                new EffectContext 
                { 
                    RootId = rootId,
                    Source = source, 
                    Target = target,
                    TargetContext = targetContext
                },
                new EffectModifiers(),
                new EffectResolveOrder()
            );

            return entity;
        }

        public static Entity CreateEffect(CommandBuffer commandBuffer, Entity source, Entity target, int durationTicks, EffectLifetimeKind lifetimeKind, int periodTicks = 0, Entity targetContext = default, GasClockId clockId = GasClockId.FixedFrame, GasConditionHandle expireCondition = default)
        {
            return CreateEffect(commandBuffer, rootId: 0, source, target, durationTicks, lifetimeKind, periodTicks, targetContext, clockId, expireCondition);
        }

        public static Entity CreateEffect(CommandBuffer commandBuffer, int rootId, Entity source, Entity target, int durationTicks, EffectLifetimeKind lifetimeKind, int periodTicks = 0, Entity targetContext = default, GasClockId clockId = GasClockId.FixedFrame, GasConditionHandle expireCondition = default)
        {
            var entity = commandBuffer.Create(GameplayEffectArchetype);
            var ge = new GameplayEffect
            {
                LifetimeKind = lifetimeKind,
                ClockId = clockId,
                TotalTicks = durationTicks,
                RemainingTicks = durationTicks,
                PeriodTicks = periodTicks,
                NextTickAtTick = 0,
                ExpiresAtTick = 0,
                ExpireCondition = expireCondition
            };
            ge.State = EffectState.Pending;
            commandBuffer.Set(entity, ge);
            commandBuffer.Set(entity, new EffectContext
            {
                RootId = rootId,
                Source = source,
                Target = target,
                TargetContext = targetContext
            });
            commandBuffer.Set(entity, new EffectModifiers());
            commandBuffer.Set(entity, new EffectResolveOrder());
            return entity;
        }

        public static void CreateEffects(CommandBuffer commandBuffer, Span<Entity> output, Entity source, Entity target, int durationTicks, EffectLifetimeKind lifetimeKind, int periodTicks = 0)
        {
            for (int i = 0; i < output.Length; i++)
            {
                output[i] = CreateEffect(commandBuffer, source, target, durationTicks, lifetimeKind, periodTicks);
            }
        }

        public static void CreateEffects(CommandBuffer commandBuffer, Span<Entity> output, ReadOnlySpan<EffectCreateCommand> commands)
        {
            for (int i = 0; i < output.Length; i++)
            {
                var cmd = commands[i];
                output[i] = CreateEffect(commandBuffer, cmd.Source, cmd.Target, cmd.DurationTicks, cmd.LifetimeKind, cmd.PeriodTicks, cmd.TargetContext, cmd.ClockId, cmd.ExpireCondition);
            }
        }

        public static void AddModifier(World world, Entity effectEntity, int attrId, ModifierOp op, float value)
        {
            ref var mods = ref world.Get<EffectModifiers>(effectEntity);
            mods.Add(attrId, op, value);
        }
    }
}
