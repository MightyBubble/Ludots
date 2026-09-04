using Sango.Core.Player;
using System.Collections.Generic;
using TKNewtonsoft.Json;
using TKNewtonsoft.Json.Linq;
using UnityEngine;

namespace Sango.Core
{
    /// <summary>
    /// 外交事件管理器
    /// </summary>
    [GameSystem(order = 101, nickName = "DiplomacyEventManager")]
    public class DiplomacyEventManager : GameSystem
    {
        /// <summary>
        /// 外交事件列表
        /// </summary>
        private List<DiplomacyEvent> _diplomacyEvents;

        /// <summary>
        /// 当前回合的事件触发次数
        /// </summary>
        private int _eventTriggerCount;



        /// <summary>
        /// 当前回合标识符
        /// </summary>
        private int _currentTurn;

        /// <summary>
        /// 初始化外交事件管理器
        /// </summary>
        /// <remarks>
        /// M3.e 激活(原注释设计的落地):事件面不再硬编码 InitDefaultEvents 的 5 条,
        /// 改由内容表驱动(Data/DiplomacyEvent/*.json,18 张;表 1–5 与原硬编码 5 条
        /// 逐一对应,是同一设计的数据化扩展)。触发面照原注释:回合开始(OnTurnStart)
        /// 重置计数并为全部存活势力对检查事件;关系落在 [MinRelation, MaxRelation] 且
        /// GameRandom.Chance(Probability) 命中即触发,每回合上限 3 次。
        /// </remarks>
        public override void Init()
        {
            _diplomacyEvents = new List<DiplomacyEvent>();
            // 初始化事件计数器
            _eventTriggerCount = 0;
            // 初始化外交事件(数据表版 InitDefaultEvents)
            LoadEventTables();
            // 注册回合开始事件监听(仅在回合开始时触发)
            GameEvent.OnTurnStart += OnTurnStart;
        }

        /// <summary>
        /// 从内容表加载外交事件(Data/DiplomacyEvent/*.json)。Effect 按 EffectType
        /// 分派(映射见 ApplyEffect);加载零表时保持空事件面(目录缺席的资产语义,
        /// 与 GameData.LoadCommonData 的目录缺席空跑一致)。
        /// </summary>
        void LoadEventTables()
        {
            var tables = new List<(int Id, DiplomacyEvent Event)>();
            Sango.Directory.EnumFiles(Path.ContentRootPath + "/Data/DiplomacyEvent", "*.json",
                System.IO.SearchOption.TopDirectoryOnly, file =>
                {
                    DiplomacyEventTable? table = JsonConvert.DeserializeObject<DiplomacyEventTable>(File.ReadAllText(file));
                    if (table == null || string.IsNullOrEmpty(table.EffectType))
                    {
                        throw new System.InvalidOperationException(
                            $"DiplomacyEvent table '{file}' is not a valid event row (missing EffectType).");
                    }

                    tables.Add((table.Id, new DiplomacyEvent
                    {
                        Id = table.Id,
                        Name = table.Name ?? string.Empty,
                        Description = table.Description ?? string.Empty,
                        MinRelation = table.MinRelation,
                        MaxRelation = table.MaxRelation,
                        Probability = table.Probability,
                        EffectType = table.EffectType,
                        EffectParams = table.EffectParams ?? new JObject(),
                    }));
                });

            // 表序 = 触发检查序:按 Id 升序(文件系统枚举序不做确定性假设)。
            tables.Sort((a, b) => a.Id.CompareTo(b.Id));
            foreach ((_, DiplomacyEvent diplomacyEvent) in tables)
            {
                _diplomacyEvents.Add(diplomacyEvent);
            }
        }

        /// <summary>
        /// 回合开始事件处理
        /// </summary>
        /// <param name="scenario">游戏场景</param>
        private void OnTurnStart(Scenario scenario)
        {
            // 回合开始时重置事件计数器
            _eventTriggerCount = 0;

            // 为所有势力检查外交事件
            CheckEventsForAllForces(scenario);
        }

        /// <summary>
        /// 检查并触发外交事件
        /// </summary>
        /// <param name="forceA">势力A</param>
        /// <param name="forceB">势力B</param>
        public void CheckAndTriggerEvents(Force forceA, Force forceB)
        {
            if (forceA == null || forceB == null || forceA == forceB)
                return;

            DiplomacyManager diplomacyManager = GameSystem.GetSystem<DiplomacyManager>();
            int relation = diplomacyManager.GetRelation(forceA, forceB);

            foreach (DiplomacyEvent e in _diplomacyEvents)
            {
                // 检查关系是否符合条件
                if (relation >= e.MinRelation && relation <= e.MaxRelation)
                {
                    // 检查概率
                    if (GameRandom.Chance(e.Probability))
                    {
                        // 触发事件
                        ApplyEffect(e, forceA, forceB);
                        // 增加事件计数器
                        _eventTriggerCount++;
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// 事件效果落地。EffectType → 内核面的映射:
        ///   AddRelation / ReduceRelation / 复合关系事件(CulturalExchange、
        ///   CommonCelebration、BorderTrade、TechnicalAssistance、EconomicAssistance):
        ///     DiplomacyManager.Add/ReduceRelation,数值取表 EffectParams(RelationValue,
        ///     缺省 Value);复合型的 GoldValue/MinGold/MaxGold 等附带参数只入消息文本
        ///     ——上游从未实现其内核语义,不在此发明;
        ///   Trade / AllianceRequest / TruceRequest:照原注释设计调 PerformDiplomacyAction
        ///     (内核无对应 DiplomacyAction 实现时返回 false,静默不落地,M3.d 停用面同源);
        ///   TechniqueExchange / RequestTroops / Marriage:提议型事件,无注释原设计、无
        ///     内核动作实现,仅入消息流(M3.f 激活对应外交动作时补映射)。
        /// </summary>
        static void ApplyEffect(DiplomacyEvent diplomacyEvent, Force sender, Force receiver)
        {
            DiplomacyManager diplomacyManager = GameSystem.GetSystem<DiplomacyManager>();
            JToken parameters = diplomacyEvent.EffectParams.Root;
            int relationValue = parameters.Value<int?>("RelationValue") ?? parameters.Value<int?>("Value") ?? 0;
            switch (diplomacyEvent.EffectType)
            {
                case "AddRelation":
                    diplomacyManager.AddRelation(sender, receiver, relationValue);
                    PlayerMessage.AddTextMessage(
                        $"{receiver.ColorName}派遣使者访问{sender.ColorName}，带来了友好的问候，关系增加了{relationValue}点！",
                        sender, sender.CapitalCity.x, sender.CapitalCity.y);
                    break;
                case "ReduceRelation":
                    diplomacyManager.ReduceRelation(sender, receiver, relationValue);
                    PlayerMessage.AddTextMessage(
                        $"{sender.ColorName}与{receiver.ColorName}在边境发生了冲突，关系减少了{relationValue}点！",
                        sender, sender.CapitalCity.x, sender.CapitalCity.y);
                    break;
                case "CulturalExchange":
                case "CommonCelebration":
                case "BorderTrade":
                case "TechnicalAssistance":
                case "EconomicAssistance":
                    diplomacyManager.AddRelation(sender, receiver, relationValue);
                    PlayerMessage.AddTextMessage(
                        $"[外交事件]{diplomacyEvent.Name}:{diplomacyEvent.Description}（{sender.ColorName}与{receiver.ColorName}，关系+{relationValue}）",
                        sender, sender.CapitalCity.x, sender.CapitalCity.y);
                    break;
                case "Trade":
                {
                    bool success = diplomacyManager.PerformDiplomacyAction(DiplomacyActionType.Trade, sender, receiver);
                    if (success)
                    {
                        PlayerMessage.AddTextMessage(
                            $"{receiver.ColorName}向{sender.ColorName}提出了贸易合作的提议，双方达成了通商协议！",
                            sender, sender.CapitalCity.x, sender.CapitalCity.y);
                    }

                    break;
                }
                case "AllianceRequest":
                {
                    diplomacyManager.PerformDiplomacyAction(DiplomacyActionType.AllianceRequest, receiver, sender);
                    PlayerMessage.AddTextMessage(
                        $"{receiver.ColorName}邀请{sender.ColorName}结成同盟！",
                        sender, sender.CapitalCity.x, sender.CapitalCity.y);
                    break;
                }
                case "TruceRequest":
                {
                    diplomacyManager.PerformDiplomacyAction(DiplomacyActionType.TruceRequest, receiver, sender);
                    PlayerMessage.AddTextMessage(
                        $"{receiver.ColorName}请求与{sender.ColorName}停战！",
                        sender, sender.CapitalCity.x, sender.CapitalCity.y);
                    break;
                }
                case "TechniqueExchange":
                case "RequestTroops":
                case "Marriage":
                    PlayerMessage.AddTextMessage(
                        $"[外交事件]{diplomacyEvent.Name}:{diplomacyEvent.Description}（{sender.ColorName} → {receiver.ColorName}）",
                        sender, sender.CapitalCity.x, sender.CapitalCity.y);
                    break;
                default:
                    throw new System.InvalidOperationException(
                        $"DiplomacyEvent table row {diplomacyEvent.Id} ('{diplomacyEvent.Name}') has unmapped EffectType '{diplomacyEvent.EffectType}'.");
            }
        }

        /// <summary>
        /// 为所有势力检查外交事件
        /// </summary>
        /// <param name="scenario">游戏场景</param>
        public void CheckEventsForAllForces(Scenario scenario)
        {
            // 检查是否已经达到本回合事件触发上限
            if (_eventTriggerCount >= 3)
                return;

            // 遍历所有势力对
            for (int i = 0; i < scenario.forceSet.Count; i++)
            {
                Force forceA = scenario.forceSet[i];
                // 检查forceA是否为null
                if (forceA == null) continue;
                if (!forceA.IsAlive) continue;

                for (int j = i + 1; j < scenario.forceSet.Count; j++)
                {
                    Force forceB = scenario.forceSet[j];
                    // 检查forceB是否为null
                    if (forceB == null) continue;
                    if (!forceB.IsAlive) continue;

                    // 检查并触发事件
                    CheckAndTriggerEvents(forceA, forceB);

                    // 检查是否已经达到本回合事件触发上限
                    if (_eventTriggerCount >= 3)
                        return;
                }
            }
        }
    }

    /// <summary>
    /// 外交事件(Data/DiplomacyEvent 表行的反序列化面)
    /// </summary>
    public sealed class DiplomacyEventTable
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public int MinRelation { get; set; }
        public int MaxRelation { get; set; }
        public int Probability { get; set; }
        public string EffectType { get; set; }
        public JObject EffectParams { get; set; }
    }

    /// <summary>
    /// 外交事件
    /// </summary>
    public class DiplomacyEvent
    {
        /// <summary>
        /// 事件ID
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// 事件名称
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// 事件描述
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// 最小关系要求
        /// </summary>
        public int MinRelation { get; set; }

        /// <summary>
        /// 最大关系要求
        /// </summary>
        public int MaxRelation { get; set; }

        /// <summary>
        /// 触发概率
        /// </summary>
        public int Probability { get; set; }

        /// <summary>
        /// 表驱动效果型(M3.e:Data/DiplomacyEvent 的 EffectType;ApplyEffect 按此分派,
        /// 取代原版硬编码事件的 Effect 委托)
        /// </summary>
        public string EffectType { get; set; }

        /// <summary>
        /// 表驱动效果参数(EffectParams 节点原样保留,ApplyEffect 现地取值)
        /// </summary>
        public JObject EffectParams { get; set; }
    }
}
