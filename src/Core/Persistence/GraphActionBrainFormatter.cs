using System;
using Ludots.Core.Gameplay.GraphBrains;
using MessagePack;
using MessagePack.Formatters;

namespace Ludots.Core.Persistence
{
    /// <summary>
    /// GraphActionBrain carries strings and default-entry arrays, so the auto unmanaged
    /// channel cannot cover it; persistence keeps the authored binding fields and the
    /// birth blackboard defaults (host re-seeds them when the brain slot is allocated).
    /// </summary>
    public sealed class GraphActionBrainFormatter : IMessagePackFormatter<GraphActionBrain>, ILudotsPersistenceComponentFormatter
    {
        public Type ComponentType => typeof(GraphActionBrain);

        public void Serialize(ref MessagePackWriter writer, GraphActionBrain value, MessagePackSerializerOptions options)
        {
            writer.WriteArrayHeader(5);
            writer.Write(value.HfsmId ?? string.Empty);
            writer.Write(value.BtId ?? string.Empty);
            writer.Write(value.ScriptKey ?? string.Empty);
            writer.Write(value.ThinkEveryNTicks);

            writer.WriteArrayHeader(value.BlackboardIntDefaults?.Length ?? 0);
            if (value.BlackboardIntDefaults != null)
            {
                for (int i = 0; i < value.BlackboardIntDefaults.Length; i++)
                {
                    writer.WriteArrayHeader(2);
                    writer.Write(value.BlackboardIntDefaults[i].Key ?? string.Empty);
                    writer.Write(value.BlackboardIntDefaults[i].Value);
                }
            }

            writer.WriteArrayHeader(value.BlackboardEntityDefaults?.Length ?? 0);
            if (value.BlackboardEntityDefaults != null)
            {
                for (int i = 0; i < value.BlackboardEntityDefaults.Length; i++)
                {
                    writer.Write(value.BlackboardEntityDefaults[i] ?? string.Empty);
                }
            }
        }

        public GraphActionBrain Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
        {
            int header = reader.ReadArrayHeader();
            if (header != 5)
            {
                throw new MessagePackSerializationException(
                    $"GraphActionBrain payload expects 5 fields, got {header}.");
            }

            var brain = new GraphActionBrain
            {
                HfsmId = reader.ReadString() ?? string.Empty,
                BtId = reader.ReadString() ?? string.Empty,
                ScriptKey = reader.ReadString() ?? string.Empty,
                ThinkEveryNTicks = reader.ReadInt32(),
            };

            int intCount = reader.ReadArrayHeader();
            if (intCount == 0)
            {
                brain.BlackboardIntDefaults = Array.Empty<(string, int)>();
            }
            else
            {
                var ints = new (string Key, int Value)[intCount];
                for (int i = 0; i < intCount; i++)
                {
                    int pair = reader.ReadArrayHeader();
                    if (pair != 2)
                    {
                        throw new MessagePackSerializationException(
                            $"GraphActionBrain int default expects a 2-field pair, got {pair}.");
                    }
                    ints[i].Key = reader.ReadString() ?? string.Empty;
                    ints[i].Value = reader.ReadInt32();
                }
                brain.BlackboardIntDefaults = ints;
            }

            int entityCount = reader.ReadArrayHeader();
            if (entityCount == 0)
            {
                brain.BlackboardEntityDefaults = Array.Empty<string>();
            }
            else
            {
                var entities = new string[entityCount];
                for (int i = 0; i < entityCount; i++)
                {
                    entities[i] = reader.ReadString() ?? string.Empty;
                }
                brain.BlackboardEntityDefaults = entities;
            }

            return brain;
        }
    }
}
