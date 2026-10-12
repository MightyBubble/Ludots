using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ludots.Core.Navigation.NavMesh.Config
{
    public sealed class NavMeshProfileCatalogConverter : JsonConverter<List<NavMeshAgentProfileConfig>>
    {
        public override List<NavMeshAgentProfileConfig> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var items = IdKeyedCatalog.Read<NavMeshAgentProfileConfig>(
                ref reader,
                options,
                "NavMeshBakeConfig.profiles",
                static (item, id) => item.Id = id);
            NavSchemaOrder.SortByKey(items, static profile => profile.Id);
            return items;
        }

        public override void Write(Utf8JsonWriter writer, List<NavMeshAgentProfileConfig> value, JsonSerializerOptions options)
            => throw new NotSupportedException("NavMeshBakeConfig.profiles 只从以 id 为键的对象读入。");
    }

    public sealed class NavMeshAreaCatalogConverter : JsonConverter<List<NavMeshAreaConfig>>
    {
        public override List<NavMeshAreaConfig> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var items = IdKeyedCatalog.Read<NavMeshAreaConfig>(
                ref reader,
                options,
                "NavMeshBakeConfig.areas",
                static (item, id) => item.Id = id);
            NavSchemaOrder.SortByAreaId(items);
            return items;
        }

        public override void Write(Utf8JsonWriter writer, List<NavMeshAreaConfig> value, JsonSerializerOptions options)
            => throw new NotSupportedException("NavMeshBakeConfig.areas 只从以 id 为键的对象读入。");
    }

    internal static class IdKeyedCatalog
    {
        public static List<T> Read<T>(ref Utf8JsonReader reader, JsonSerializerOptions options, string path, Action<T, string> setId)
            where T : class
        {
            if (reader.TokenType == JsonTokenType.StartArray)
            {
                throw new JsonException($"{path} 以 id 为键的对象，不再使用数组。");
            }

            if (reader.TokenType != JsonTokenType.StartObject)
            {
                throw new JsonException($"{path} 必须是以 id 为键的对象。");
            }

            var items = new List<T>();
            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                if (reader.TokenType != JsonTokenType.PropertyName)
                {
                    throw new JsonException($"{path} 必须是以 id 为键的对象。");
                }

                string id = reader.GetString();
                if (string.IsNullOrWhiteSpace(id))
                {
                    throw new JsonException($"{path} 的键不能为空。");
                }

                reader.Read();
                if (reader.TokenType == JsonTokenType.Null)
                {
                    throw new JsonException($"{path}.{id} 为 null。以 id 为键的对象不接受 null。");
                }

                T item = JsonSerializer.Deserialize<T>(ref reader, options)
                    ?? throw new JsonException($"{path}.{id} 反序列化为空。");
                setId(item, id);
                items.Add(item);
            }

            return items;
        }
    }
}
