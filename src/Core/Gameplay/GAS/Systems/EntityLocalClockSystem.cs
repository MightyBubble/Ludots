using System;
using Arch.Core;
using Arch.Core.Extensions;
using Arch.Buffer;
using Ludots.Core.Engine.TimeFlow;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;

namespace Ludots.Core.Gameplay.GAS.Systems
{
    public sealed class EntityLocalClockSystem : BaseSystem<World, float>
    {
        private static readonly QueryDescription _query = new QueryDescription()
            .WithAll<EntityLocalClock, AttributeBuffer>();
        private static readonly QueryDescription _missingAttributeBufferQuery = new QueryDescription()
            .WithAll<EntityLocalClock>()
            .WithNone<AttributeBuffer>();

        private readonly GasClockStepPolicy _stepPolicy;
        private readonly int _scalePermilleAttributeId;

        public EntityLocalClockSystem(World world, GasClockStepPolicy stepPolicy, int scalePermilleAttributeId) : base(world)
        {
            _stepPolicy = stepPolicy ?? throw new ArgumentNullException(nameof(stepPolicy));
            if ((uint)scalePermilleAttributeId >= AttributeBuffer.MAX_ATTRS)
            {
                throw new ArgumentOutOfRangeException(nameof(scalePermilleAttributeId));
            }

            _scalePermilleAttributeId = scalePermilleAttributeId;
        }

        public override void Update(in float dt)
        {
            if (World.CountEntities(in _missingAttributeBufferQuery) > 0)
            {
                throw new InvalidOperationException("EntityLocalClock requires AttributeBuffer.time.scale_permille.");
            }

            var job = new UpdateJob
            {
                ScalePermilleAttributeId = _scalePermilleAttributeId,
                ConsumedGlobalSteps = _stepPolicy.LastConsumedSteps
            };
            World.InlineEntityQuery<UpdateJob, EntityLocalClock, AttributeBuffer>(in _query, ref job);
        }

        private struct UpdateJob : IForEachWithEntity<EntityLocalClock, AttributeBuffer>
        {
            public int ScalePermilleAttributeId;
            public int ConsumedGlobalSteps;

            public void Update(Entity entity, ref EntityLocalClock clock, ref AttributeBuffer attributes)
            {
                if (!attributes.HasAttribute(ScalePermilleAttributeId))
                {
                    throw new InvalidOperationException("EntityLocalClock requires AttributeBuffer.time.scale_permille.");
                }

                int localScalePermille = ReadScalePermille(in attributes, ScalePermilleAttributeId);
                if (ConsumedGlobalSteps <= 0 || localScalePermille <= 0)
                {
                    return;
                }

                int inputPermille = checked(ConsumedGlobalSteps * localScalePermille);
                int steps = PermilleStepAccumulator.Consume(
                    ref clock.AccumulatorPermille,
                    inputPermille,
                    TimeFlowService.DefaultScalePermille);
                clock.LocalStep = checked(clock.LocalStep + steps);
            }

            private static int ReadScalePermille(in AttributeBuffer attributes, int attributeId)
            {
                float raw = attributes.GetCurrent(attributeId);
                if (!float.IsFinite(raw))
                {
                    throw new InvalidOperationException("AttributeBuffer.time.scale_permille must be finite.");
                }

                float rounded = MathF.Round(raw);
                if (MathF.Abs(raw - rounded) > 0.001f)
                {
                    throw new InvalidOperationException("AttributeBuffer.time.scale_permille must be an integer permille value.");
                }

                if (rounded < 0f)
                {
                    throw new InvalidOperationException("AttributeBuffer.time.scale_permille must be >= 0.");
                }

                return TimeFlowService.ClampScalePermille((long)rounded);
            }
        }
    }
}
