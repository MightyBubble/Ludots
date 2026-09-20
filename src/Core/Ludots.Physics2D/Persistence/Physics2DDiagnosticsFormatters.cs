using System;
using Ludots.Core.Physics2D.Components;
using Ludots.Core.Persistence;
using MessagePack;
using MessagePack.Formatters;

namespace Ludots.Core.Physics2D.Persistence
{
    /// <summary>
    /// Physics diagnostics singletons carry wall-clock derived fields (stopwatch-measured update
    /// time, FixedTotalTime-derived last step time). Persistence writes only deterministic config
    /// echoes; owning systems rewrite the volatile fields on their next update.
    /// </summary>
    public sealed class Physics2DPerfStatsFormatter : IMessagePackFormatter<Physics2DPerfStats>, ILudotsPersistenceComponentFormatter
    {
        public Type ComponentType => typeof(Physics2DPerfStats);

        public void Serialize(ref MessagePackWriter writer, Physics2DPerfStats value, MessagePackSerializerOptions options)
        {
            writer.WriteArrayHeader(4);
            writer.Write(value.FixedHz);
            writer.Write(value.PhysicsHz);
            writer.Write(value.BroadphaseStrategy);
            writer.Write(value.BroadphaseCellSizeCm);
        }

        public Physics2DPerfStats Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
        {
            int header = reader.ReadArrayHeader();
            if (header != 4)
            {
                throw new MessagePackSerializationException($"Physics2DPerfStats payload expected 4 fields but found {header}.");
            }

            return new Physics2DPerfStats
            {
                FixedHz = reader.ReadInt32(),
                PhysicsHz = reader.ReadInt32(),
                BroadphaseStrategy = reader.ReadInt32(),
                BroadphaseCellSizeCm = reader.ReadInt32(),
            };
        }
    }
}
