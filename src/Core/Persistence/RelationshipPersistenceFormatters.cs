using Arch.Core;
using Arch.Relationships;
using Ludots.Core.Gameplay.Relationships;
using MessagePack;
using MessagePack.Formatters;
using System;
using System.Collections.Generic;

namespace Ludots.Core.Persistence
{
    internal sealed class RelationshipEdgeFormatter : IMessagePackFormatter<RelationshipEdge>
    {
        public void Serialize(ref MessagePackWriter writer, RelationshipEdge value, MessagePackSerializerOptions options)
        {
            writer.WriteArrayHeader(3);
            writer.Write(value.Flags);
            writer.Write(value.Version);

            ReadOnlySpan<short> metrics = value.Metrics;
            writer.WriteArrayHeader(metrics.Length);
            for (int i = 0; i < metrics.Length; i++)
            {
                writer.Write(metrics[i]);
            }
        }

        public RelationshipEdge Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
        {
            int header = reader.ReadArrayHeader();
            if (header != 3)
            {
                throw new MessagePackSerializationException(
                    $"RelationshipEdge payload expected 3 fields but found {header}.");
            }

            var edge = default(RelationshipEdge);
            edge.Flags = reader.ReadUInt32();
            edge.Version = reader.ReadInt32();

            int metricCount = reader.ReadArrayHeader();
            for (int i = 0; i < metricCount; i++)
            {
                edge.SetMetric(i, reader.ReadInt16());
            }

            return edge;
        }
    }

    internal sealed class RelationshipEdgeSetFormatter : IMessagePackFormatter<RelationshipEdgeSet>
    {
        public void Serialize(ref MessagePackWriter writer, RelationshipEdgeSet value, MessagePackSerializerOptions options)
        {
            writer.WriteArrayHeader(value.Count);
            for (int i = 0; i < value.Count; i++)
            {
                if (!value.TryGetAt(i, out int typeId, out RelationshipEdge edge))
                {
                    throw new MessagePackSerializationException(
                        $"RelationshipEdgeSet entry index {i} is outside the persisted edge set.");
                }

                writer.WriteArrayHeader(2);
                writer.Write(typeId);
                MessagePackSerializer.Serialize(ref writer, edge, options);
            }
        }

        public RelationshipEdgeSet Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
        {
            int count = reader.ReadArrayHeader();
            var set = default(RelationshipEdgeSet);
            for (int i = 0; i < count; i++)
            {
                int header = reader.ReadArrayHeader();
                if (header != 2)
                {
                    throw new MessagePackSerializationException(
                        $"RelationshipEdgeSet entry expected 2 fields but found {header}.");
                }

                int typeId = reader.ReadInt32();
                RelationshipEdge edge = MessagePackSerializer.Deserialize<RelationshipEdge>(ref reader, options);
                set.Set(typeId, edge);
            }

            return set;
        }
    }

    internal sealed class RelationshipComponentFormatter<T> :
        IMessagePackFormatter<Relationship<T>>,
        ILudotsPersistenceComponentFormatter
    {
        public Type ComponentType => typeof(Relationship<T>);

        public void Serialize(ref MessagePackWriter writer, Relationship<T> value, MessagePackSerializerOptions options)
        {
            if (value == null)
            {
                writer.WriteNil();
                return;
            }

            writer.WriteArrayHeader(value.Elements.Count);
            foreach (KeyValuePair<Entity, T> entry in value.Elements)
            {
                writer.WriteArrayHeader(2);
                MessagePackSerializer.Serialize(ref writer, entry.Key, options);
                MessagePackSerializer.Serialize(ref writer, entry.Value, options);
            }
        }

        public Relationship<T> Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
        {
            if (reader.TryReadNil())
            {
                return null!;
            }

            int count = reader.ReadArrayHeader();
            var elements = new SortedList<Entity, T>();
            for (int i = 0; i < count; i++)
            {
                int header = reader.ReadArrayHeader();
                if (header != 2)
                {
                    throw new MessagePackSerializationException(
                        $"Relationship entry expected 2 fields but found {header}.");
                }

                Entity target = MessagePackSerializer.Deserialize<Entity>(ref reader, options);
                T relationship = MessagePackSerializer.Deserialize<T>(ref reader, options);
                elements.Add(target, relationship);
            }

            return new Relationship<T>(elements);
        }
    }

    internal sealed class InRelationshipFormatter : IMessagePackFormatter<InRelationship>
    {
        public void Serialize(ref MessagePackWriter writer, InRelationship value, MessagePackSerializerOptions options)
        {
            writer.Write(value.ComponentTypeId);
        }

        public InRelationship Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
        {
            return new InRelationship(reader.ReadInt32());
        }
    }
}
