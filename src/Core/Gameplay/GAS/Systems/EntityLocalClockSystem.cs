using System;
using Arch.Core;
using Arch.System;
using Ludots.Core.Engine.TimeFlow;
using Ludots.Core.Gameplay.GAS.Bindings;
using Ludots.Core.Gameplay.GAS.Components;

namespace Ludots.Core.Gameplay.GAS.Systems
{
    public sealed class EntityLocalClockSystem : BaseSystem<World, float>
    {
        private static readonly QueryDescription _query = new QueryDescription()
            .WithAll<EntityLocalClock>();

        private readonly GasClockStepPolicy _stepPolicy;

        public EntityLocalClockSystem(World world, GasClockStepPolicy stepPolicy) : base(world)
        {
            _stepPolicy = stepPolicy ?? throw new ArgumentNullException(nameof(stepPolicy));
        }

        public override void Update(in float dt)
        {
            var job = new UpdateJob
            {
                ConsumedGlobalSteps = _stepPolicy.LastConsumedSteps
            };
            World.InlineEntityQuery<UpdateJob, EntityLocalClock>(in _query, ref job);
        }

        private struct UpdateJob : IForEachWithEntity<EntityLocalClock>
        {
            public int ConsumedGlobalSteps;

            public void Update(Entity entity, ref EntityLocalClock clock)
            {
                if (clock.ScaleLanded == 0)
                {
                    throw new InvalidOperationException(
                        $"EntityLocalClock scale has not been landed by {GasSinkNames.EntityScalePermille}. entity={entity.Id}.");
                }

                if ((uint)clock.ScalePermille > (uint)TimeFlowService.MaxScalePermille)
                {
                    throw new InvalidOperationException(
                        $"EntityLocalClock.ScalePermille must be in [0, {TimeFlowService.MaxScalePermille}]. entity={entity.Id}.");
                }

                if (ConsumedGlobalSteps <= 0 || clock.ScalePermille <= 0)
                {
                    return;
                }

                int inputPermille = checked(ConsumedGlobalSteps * clock.ScalePermille);
                int steps = PermilleStepAccumulator.Consume(
                    ref clock.AccumulatorPermille,
                    inputPermille,
                    TimeFlowService.DefaultScalePermille);
                clock.LocalStep = checked(clock.LocalStep + steps);
            }
        }
    }
}
