using Ludots.Core.MassNavigation.Runtime;
using MessagePack;
using MessagePack.Formatters;

namespace Ludots.Core.Persistence
{
    public sealed class MassNavigationAgentFormatter : IMessagePackFormatter<MassNavigationAgent>, ILudotsPersistenceComponentFormatter
    {
        public Type ComponentType => typeof(MassNavigationAgent);

        public void Serialize(ref MessagePackWriter writer, MassNavigationAgent value, MessagePackSerializerOptions options)
        {
            writer.WriteArrayHeader(2);
            writer.Write(value.ProfileId);
            writer.Write(value.AgentTypeId);
        }

        public MassNavigationAgent Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
        {
            int count = reader.ReadArrayHeader();
            if (count != 2)
            {
                throw new MessagePackSerializationException($"MassNavigationAgent expects 2 fields, got {count}.");
            }

            int profileId = reader.ReadInt32();
            string agentTypeId = reader.ReadString()!;
            return new MassNavigationAgent { ProfileId = profileId, AgentTypeId = agentTypeId };
        }
    }
}
