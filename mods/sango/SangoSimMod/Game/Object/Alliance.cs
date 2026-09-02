using TKNewtonsoft.Json;
using System.Text;

namespace Sango.Core
{
    /// <summary>
    /// 联盟类型
    /// </summary>
    public enum AllianceType
    {
        /// <summary>
        /// 同盟
        /// </summary>
        Alliance = 1,
        /// <summary>
        /// 停战协议
        /// </summary>
        Truce = 2,
        /// <summary>
        /// 通商协议
        /// </summary>
        Trade = 3
    }

    [JsonObject(MemberSerialization.OptIn)]
    public class Alliance : SangoObject
    {

        /// <summary>
        /// 部队列表
        /// </summary>
        [JsonConverter(typeof(SangoObjectListIDConverter<Force>))]
        [JsonProperty]
        public SangoObjectList<Force> ForceList = new SangoObjectList<Force>();

        [JsonProperty] public int leftCount;
        [JsonProperty] public AllianceType allianceType;

        public bool Contains(Force force)
        {
            return ForceList.Contains(force);
        }

        public override bool OnTurnStart(Scenario scenario)
        {
            leftCount--;
            IsAlive = leftCount > 0;
            if(!IsAlive )
            {
#if SANGO_DEBUG
                StringBuilder stringBuilder = new StringBuilder();
#endif
                foreach (Force force in ForceList)
                {
#if SANGO_DEBUG
                    stringBuilder.Append(force.Name);
                    stringBuilder.Append(" ");
#endif
                    force.AllianceList.Remove(this);
                    
                    // 如果是通商协议，还原黄金收入
                    if (allianceType == AllianceType.Trade)
                    {
                        force.ForEachCity(city => city.baseGainGold -= 50);
                    }
                }

#if SANGO_DEBUG
                string allianceTypeStr = allianceType == AllianceType.Alliance ? "同盟" : (allianceType == AllianceType.Truce ? "停战协议" : "通商协议");
                Sango.Log.Info($"@外交@{stringBuilder.ToString()} 的{allianceTypeStr} {Id} 于{scenario.GetDateStr()} 结束!!");
#endif

            }
            return base.OnTurnStart(scenario);
        }
    }
}
