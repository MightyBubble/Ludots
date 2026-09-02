using TKNewtonsoft.Json;
using TKNewtonsoft.Json.Linq;
using TKNewtonsoft.Json.Serialization;
using System;
using System.Collections.Generic;

namespace Sango.Core
{
    public class String2JObjectConverter : JsonConverter<JObject>
    {
        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            JObject dest = value as JObject;
            writer.WriteValue(dest.ToString());
        }
        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer, JsonProperty property, object target)
        {
            string content = serializer.Deserialize<string>(reader);
            return JsonConvert.DeserializeObject<JObject>(content);
        }
    }
}
