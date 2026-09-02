using TKNewtonsoft.Json;
using TKNewtonsoft.Json.Serialization;
using System;
using System.Collections.Generic;

namespace Sango.Core
{
    

    public class XY2CellConverter : JsonConverter<Cell>
    {
        public struct DelaySetCellValue
        {
            public object target;
            public JsonProperty property;
            public int x;
            public int y;
            public static List<DelaySetCellValue> delaySetValues_List = new List<DelaySetCellValue>();
            public static void OnScenarioPrepare(Scenario scenario)
            {
                for (int i = 0; i < delaySetValues_List.Count; i++)
                {
                    DelaySetCellValue setValue = delaySetValues_List[i];
                    var value = scenario.Map.GetCell(setValue.x, setValue.y);
                    if (value != null && setValue.property != null)
                        setValue.property.ValueProvider.SetValue(setValue.target, value);
                }
                delaySetValues_List.Clear();
                GameEvent.OnScenarioPrepare -= OnScenarioPrepare;
            }
            public static void Add(DelaySetCellValue setValue)
            {
                if (delaySetValues_List.Count == 0)
                    GameEvent.OnScenarioPrepare += OnScenarioPrepare;
                delaySetValues_List.Add(setValue);
            }
        }

        public override Cell Create(Type objectType)
        {
            return null;
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            writer.WriteStartArray();
            Cell dest = value as Cell;
            writer.WriteValue(dest.x);
            writer.WriteValue(dest.y);
            writer.WriteEndArray();
        }
        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer, JsonProperty property, object target)
        {
            Cell dest = (Cell)existingValue;
            bool xReaded = false;
            int x = 0, y = 0;
            while (reader.Read())
            {
                if (reader.TokenType == JsonToken.EndArray)
                {
                    DelaySetCellValue.Add(new DelaySetCellValue
                    {
                        x = x,
                        y = y,
                        target = target,
                        property = property,
                    });
                    return null;
                }
                else if (reader.TokenType == JsonToken.Integer)
                {
                    int v = serializer.Deserialize<int>(reader);
                    if(!xReaded)
                    {
                        x = v;
                        xReaded = true;
                    }
                    else
                    {
                        y = v;
                    }
                }
            }
            return null;
        }
    }
}
