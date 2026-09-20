using System;
using Ludots.Core.Engine.Physics2D;
using Ludots.Core.Presentation.Components;
using MessagePack;
using MessagePack.Formatters;

namespace Ludots.Core.Persistence
{
    /// <summary>
    /// Diagnostics singletons carry wall-clock fields (render interpolation alpha, frame timings,
    /// stopwatch-measured update times). Those fields differ on every capture even for a static
    /// world, so persistence writes them as zero; owning systems rewrite them on their next update.
    /// Deterministic fields (config echoes) are preserved.
    /// </summary>
    public sealed class PresentationFrameStateFormatter : IMessagePackFormatter<PresentationFrameState>, ILudotsPersistenceComponentFormatter
    {
        public Type ComponentType => typeof(PresentationFrameState);

        public void Serialize(ref MessagePackWriter writer, PresentationFrameState value, MessagePackSerializerOptions options)
        {
            writer.WriteArrayHeader(2);
            writer.Write(value.Enabled);
            writer.Write(value.FixedDeltaTime);
        }

        public PresentationFrameState Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
        {
            int header = reader.ReadArrayHeader();
            if (header != 2)
            {
                throw new MessagePackSerializationException($"PresentationFrameState payload expected 2 fields but found {header}.");
            }

            return new PresentationFrameState
            {
                Enabled = reader.ReadBoolean(),
                FixedDeltaTime = reader.ReadSingle(),
            };
        }
    }

    public sealed class Physics2DRuntimeStateFormatter : IMessagePackFormatter<Physics2DRuntimeState>, ILudotsPersistenceComponentFormatter
    {
        public Type ComponentType => typeof(Physics2DRuntimeState);

        public void Serialize(ref MessagePackWriter writer, Physics2DRuntimeState value, MessagePackSerializerOptions options)
        {
            writer.WriteArrayHeader(2);
            writer.Write(value.PhysicsStepDuration);
            writer.Write(value.AnyAwakeDynamicBodies);
        }

        public Physics2DRuntimeState Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
        {
            int header = reader.ReadArrayHeader();
            if (header != 2)
            {
                throw new MessagePackSerializationException($"Physics2DRuntimeState payload expected 2 fields but found {header}.");
            }

            return new Physics2DRuntimeState
            {
                PhysicsStepDuration = reader.ReadSingle(),
                AnyAwakeDynamicBodies = reader.ReadBoolean(),
            };
        }
    }
}
