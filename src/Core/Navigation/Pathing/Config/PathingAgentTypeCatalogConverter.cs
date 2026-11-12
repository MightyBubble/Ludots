using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ludots.Core.Navigation.NavMesh.Config;

namespace Ludots.Core.Navigation.Pathing.Config
{
    public sealed class PathingAgentTypeCatalogConverter : JsonConverter<List<PathingAgentTypeConfig>>
    {
        public override List<PathingAgentTypeConfig> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var items = IdKeyedCatalog.Read<PathingAgentTypeConfig>(
                ref reader,
                options,
                "PathingConfig.agentTypes",
                static (item, id) => item.Id = id);
            return items;
        }

        public override void Write(Utf8JsonWriter writer, List<PathingAgentTypeConfig> value, JsonSerializerOptions options)
            => throw new NotSupportedException("PathingConfig.agentTypes 只从以 id 为键的对象读入。");
    }
}
