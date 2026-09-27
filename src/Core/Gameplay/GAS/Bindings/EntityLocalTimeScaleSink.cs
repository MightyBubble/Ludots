using System;
using Arch.Core;
using Arch.Core.Extensions;
using Arch.System;
using Ludots.Core.Engine.TimeFlow;
using Ludots.Core.Gameplay.GAS.Components;

namespace Ludots.Core.Gameplay.GAS.Bindings
{
    public sealed class EntityLocalTimeScaleSink : IAttributeSink
    {
        private readonly QueryDescription _query = new QueryDescription().WithAll<AttributeBuffer, EntityLocalClock>();
        private readonly QueryDescription _missingAttributeBufferQuery = new QueryDescription()
            .WithAll<EntityLocalClock>()
            .WithNone<AttributeBuffer>();

        public void ValidateBinding(byte channel, string bindingId, string relativePath)
        {
            if (channel == 0)
            {
                return;
            }

            throw new InvalidOperationException(
                $"Attribute binding '{bindingId}' in {relativePath}: sink '{GasSinkNames.EntityScalePermille}' supports channel 0; found {channel}.");
        }

        public void ValidatePolicy(
            AttributeBindingMode mode,
            AttributeBindingResetPolicy resetPolicy,
            float scale,
            string bindingId,
            string relativePath)
        {
            if (mode != AttributeBindingMode.Override ||
                resetPolicy != AttributeBindingResetPolicy.None ||
                scale != 1f)
            {
                throw new InvalidOperationException(
                    $"Attribute binding '{bindingId}' in {relativePath}: sink '{GasSinkNames.EntityScalePermille}' requires mode Override, resetPolicy None, and scale 1.");
            }
        }

        public void Apply(World world, AttributeBindingEntry[] entries, int start, int count)
        {
            if (count != 1)
            {
                throw new InvalidOperationException(
                    $"Sink '{GasSinkNames.EntityScalePermille}' requires exactly one binding; found {count}.");
            }

            if (world.CountEntities(in _missingAttributeBufferQuery) > 0)
            {
                throw new InvalidOperationException("EntityLocalClock requires AttributeBuffer.time.scale_permille.");
            }

            var job = new ApplyJob
            {
                Entries = entries,
                Start = start
            };
            world.InlineEntityQuery<ApplyJob, AttributeBuffer, EntityLocalClock>(in _query, ref job);
        }

        private struct ApplyJob : IForEachWithEntity<AttributeBuffer, EntityLocalClock>
        {
            public AttributeBindingEntry[] Entries;
            public int Start;

            public void Update(Entity entity, ref AttributeBuffer attributes, ref EntityLocalClock clock)
            {
                AttributeBindingEntry binding = Entries[Start];
                if (!attributes.HasAttribute(binding.AttributeId))
                {
                    throw new InvalidOperationException("EntityLocalClock requires AttributeBuffer.time.scale_permille.");
                }

                clock.ScalePermille = ReadScalePermille(in attributes, binding.AttributeId);
                clock.ScaleLanded = 1;
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

                if (rounded > TimeFlowService.MaxScalePermille)
                {
                    throw new InvalidOperationException($"AttributeBuffer.time.scale_permille must be <= {TimeFlowService.MaxScalePermille}.");
                }

                return (int)rounded;
            }
        }
    }
}
