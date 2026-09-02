//using TKNewtonsoft.Json;
//using TKNewtonsoft.Json.Linq;
//using System;

//namespace Sango.Core
//{
//    /// <summary>
//    /// 这个比较特殊,包括继承类
//    /// </summary>
//    /// <typeparam name="T"></typeparam>
//    public class SangoObjectMapSkillEffectConverter : JsonConverter<SangoObjectMap<SkillEffect>>
//    {
//        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
//        {
//            writer.WriteStartObject();
//            SangoObjectMap<SkillEffect> dest = value as SangoObjectMap<SkillEffect>;
//            dest.ForEach(x =>
//            {
//                writer.WritePropertyName(x.Id.ToString());
//                serializer.Serialize(writer, x);
//            });
//            writer.WriteEndObject();
//        }
//        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
//        {
//            if (existingValue == null)
//                existingValue = Create(objectType);
//            SangoObjectMap<SkillEffect> dest = existingValue as SangoObjectMap<SkillEffect>;
//            string lastPropertyName = null;
//            while (reader.Read())
//            {
//                if (reader.TokenType == JsonToken.PropertyName)
//                {
//                    lastPropertyName = reader.Value.ToString();
//                }
//                else if (reader.TokenType == JsonToken.StartObject)
//                {

//                    if (!string.IsNullOrEmpty(lastPropertyName))
//                    {
//                        int Id = int.Parse(lastPropertyName);
//                        SkillEffect exsist = dest.Get(Id);
//                        if (exsist != null)
//                        {
//                            serializer.Populate(reader, exsist);
//                            continue;
//                        }

//                        JObject jo = JObject.Load(reader); // 加载整个 JSON 对象到 JObject 中
//                        if (jo["effectType"] != null) // 检查 Name 是否存在
//                        {
//                            int effectType = jo["effectType"].Value<int>();
//                            SkillEffect skillEffect = SkillEffect.Create(effectType);
//                            JsonConvert.PopulateObject(jo.ToString(), skillEffect); // 根据需要选择反序列化的类或对象类型
//                            dest.Add(skillEffect);

//                        }
//                    }
//                }
//                else if (reader.TokenType == JsonToken.EndObject)
//                {
//                    return dest;
//                }
//            }
//            return dest;
//        }
//    }
//}
