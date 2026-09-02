using Sango.Core.Action;
using System.Collections.Generic;
using TKNewtonsoft.Json;
using TKNewtonsoft.Json.Linq;

namespace Sango.Core
{
    public class Equipment : ItemType
    {
        /// <summary>
        /// 品级
        /// </summary>
        [JsonProperty] public int grade;

        /// <summary>
        /// 发现概率
        /// </summary>
        [JsonProperty] public int discoverProbability;

        /// <summary>
        /// 统率加成
        /// </summary>
        [JsonProperty] public int commandBonus;

        /// <summary>
        /// 武力加成
        /// </summary>
        [JsonProperty] public int strengthBonus;

        /// <summary>
        /// 智力加成
        /// </summary>
        [JsonProperty] public int intelligenceBonus;

        /// <summary>
        /// 政治加成
        /// </summary>
        [JsonProperty] public int politicsBonus;

        /// <summary>
        /// 魅力加成
        /// </summary>
        [JsonProperty] public int glamourBonus;

        /// <summary>
        /// 附加特技
        /// </summary>
        [JsonProperty]
        [JsonConverter(typeof(SangoObjectListIDConverter<Feature>))]
        public SangoObjectList<Feature> features = new SangoObjectList<Feature>();

        /// <summary>
        /// 效果实体集合
        /// </summary>
        [JsonProperty]
        public TKNewtonsoft.Json.Linq.JArray actionEntities;

        public void InitActions(List<ActionBase> list, params SangoObject[] sangoObjects)
        {
            if (actionEntities == null) return;
            for (int i = 0; i < actionEntities.Count; i++)
            {
                JObject valus = actionEntities[i] as JObject;
                ActionBase action = ActionBase.Create(valus.Value<string>("class"));
                if (action != null)
                {
                    action.Init(valus, sangoObjects);
                    list.Add(action);
                }
            }
        }
    }
}
