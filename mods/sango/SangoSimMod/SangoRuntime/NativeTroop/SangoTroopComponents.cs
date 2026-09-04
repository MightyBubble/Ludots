// D-3' 部队/军团域原生实现:数据组件 + GAS 属性面。
// 组件合同(同 D-1' 城域/D-2' 武将域):全部 unmanaged struct(InlineArray 保序、定容、
// 打满即抛错不截断),经引擎持久化发现链(LudotsCorePersistenceFormatters.
// AddAutoDiscoveredUnmanagedFormatters)自动获得 world.bin 原生序列化,不建平行持久化。
// 数值(兵力/士气/携粮)不入组件——走 GAS attribute(sango.troop.*,注册进引擎
// AttributeRegistry 唯一表,值写入走 AttributeMutationOps.SetBase 正式通道;SetBase
// 自带同值幂等省略,批量同步天然节流)。任务态/技能 CD/移动路径态/编成/俘囚名单/
// 军团 AP 与 jobCounter 为组件(部队任务态显式组件化=SangoPersonMission 先例,原生读
// 面不再触碰内核 TroopMissionBehaviour 懒重建链)。

using System.Runtime.CompilerServices;

namespace Sango.Runtime
{
    /// <summary>
    /// 部队 GAS 属性注册面(同 SangoCityAttributes/SangoPersonAttributes 模式:引擎
    /// AttributeRegistry 唯一表,SangoSimModEntry.OnLoad 走 context 面,运行时/测试侧
    /// 幂等注册)。值语义 = 内核 Troop.troops/morale/food 的当前值。
    /// </summary>
    public static class SangoTroopAttributes
    {
        public const string Troops = "sango.troop.troops";
        public const string Morale = "sango.troop.morale";
        public const string Food = "sango.troop.food";

        static int _troops = Ludots.Core.Gameplay.GAS.Registry.AttributeRegistry.InvalidId;
        static int _morale = Ludots.Core.Gameplay.GAS.Registry.AttributeRegistry.InvalidId;
        static int _food = Ludots.Core.Gameplay.GAS.Registry.AttributeRegistry.InvalidId;

        public static int TroopsId => _troops;
        public static int MoraleId => _morale;
        public static int FoodId => _food;

        /// <summary>幂等注册(重复名返回既有 id;引擎表 Register 对重名显式抛错,故先查后注)。</summary>
        public static void EnsureRegistered()
        {
            _troops = Ensure(Troops);
            _morale = Ensure(Morale);
            _food = Ensure(Food);
        }

        static int Ensure(string name)
        {
            int id = Ludots.Core.Gameplay.GAS.Registry.AttributeRegistry.GetId(name);
            return id != Ludots.Core.Gameplay.GAS.Registry.AttributeRegistry.InvalidId
                ? id
                : Ludots.Core.Gameplay.GAS.Registry.AttributeRegistry.Register(name);
        }
    }

    /// <summary>部队身份:内核部队 id(Troop.Id,部队实体与内核 Troop PONO 的对账键)。</summary>
    public struct SangoTrooperIdentity
    {
        public int TroopId;
    }

    /// <summary>
    /// 部队格位组件:内核格坐标(x=北、y=东;世界位在实体的 WorldPositionCm,经
    /// MaterializeTemplate 摆位 + OnTroopEnterCell 逐格跟随,presenter owner 迁移后
    /// VisualTransform 同轴)。
    /// </summary>
    public struct SangoTroopPosition
    {
        public int CellX;
        public int CellY;
    }

    /// <summary>
    /// 部队编成组件:主将/副将(内核 Leader/Member1/Member2 的 id 面,0 = 空)、
    /// 水陆兵种 id、势力/军团/所属城反向引用(城名单的正向引用在城组件;部队侧只留
    /// 反向 id,不重复城序)。
    /// </summary>
    public struct SangoTroopComposition
    {
        public int LeaderId;
        public int Member1Id;
        public int Member2Id;
        public int LandTroopTypeId;
        public int WaterTroopTypeId;
        public int ForceId;
        public int CorpsId;
        public int BelongCityId;
    }

    /// <summary>
    /// 部队任务态显式组件(Troop.missionType/missionTarget/missionParams1/2/
    /// missionTargetCell 的组件面;SangoPersonMission 先例)。原生读写面只经本组件,
    /// 不触碰内核 TroopMissionBehaviour 懒重建链(M3.g 在案隐患的组件化根治);
    /// 内核行为体内部仍按其原语义运行(内核保留面,D-5' 终局)。
    /// </summary>
    public struct SangoTroopMission
    {
        public int MissionType;
        public int MissionTarget;
        public int MissionParams1;
        public int MissionParams2;
        public int MissionTargetCellX;
        public int MissionTargetCellY;
        public byte HasMissionTargetCell;
    }

    /// <summary>
    /// 部队技能 CD 组件(M3.a 存档补捕面的组件化):按技能表 id 记录冷却计数
    /// (内核 SkillInstance.CDCount,land/water/Strategy 三表按枚举序)。定容 24,
    /// 超容即类型化抛错。组件随 world.bin 持久化;sango 存档侧捕获面(SangoSaveParticipant
    /// troopDomain 节)由本组件语义接管,原 SangoCityPersonOrder.TroopSkillCd 退役。
    /// </summary>
    public struct SangoTroopSkillCooldowns
    {
        public const int Capacity = 24;

        /// <summary>单条冷却(技能表 id + 计数);InlineArray 元素类型=本结构。</summary>
        public struct SkillCdEntry
        {
            public int SkillId;
            public int Cd;
        }

        public int Count;

        [InlineArray(Capacity)]
        public struct SkillCdEntryList
        {
#pragma warning disable CS0169 // 字段经 InlineArray 展开访问,不直接引用
            private SkillCdEntry _element;
#pragma warning restore CS0169
        }

        public SkillCdEntryList Entries;

        /// <summary>条目读取面(InlineArray 索引器的代码面;测试/探针反射取值用)。</summary>
        public readonly int SkillIdAt(int index) => Entries[index].SkillId;

        public readonly int CdAt(int index) => Entries[index].Cd;
    }

    /// <summary>
    /// 部队移动路径态组件:内核单回合移动链的显式状态(isMoving + 本回合移动范围量;
    /// 委任移动的跨回合目标在任务态组件——missionTargetCell/missionParams)。
    /// isMoving 内核为 internal(同程序集可读);当步微移目标(tryToDest)是内核
    /// 私有演出驱动暂态,不入组件(非权威面,读模型合同 = 回合边界 + 命令 ack 前一致)。
    /// </summary>
    public struct SangoTroopMovement
    {
        public byte IsMoving;
        public int MoveRangeCount;
    }

    /// <summary>
    /// 部队俘囚名单组件(内核 Troop.captiveList 的 id 序面;捕获/释放/入城献俘都改
    /// 该名单,存档捕获节 troopCaptives 的组件载体)。
    /// </summary>
    public struct SangoTroopCaptives
    {
        public const int Capacity = 16;

        public int Count;

        [InlineArray(Capacity)]
        public struct CaptiveIdList
        {
#pragma warning disable CS0169
            private int _element;
#pragma warning restore CS0169
        }

        public CaptiveIdList PersonIds;
    }

    /// <summary>
    /// 部队回合账本组件(对拍探针):最近一次回合结算的耗粮/携粮落点/士气变化
    /// (断粮分支记录士气削减值与 30% 兵损)、出征天数(liveDays)。测试按内核
    /// Troop.OnForceTurnStart 公式独立复算断言(士气落点/耗粮逐位)。
    /// </summary>
    public struct SangoTroopLedger
    {
        public int LastFoodCost;
        public int LastFood;
        public int LastMoraleDelta;
        public int LastStarveDamage;
        public int LiveDays;
        public byte Starving;
    }

    /// <summary>军团身份:内核军团 id(Corps.Id)。</summary>
    public struct SangoCorpsIdentity
    {
        public int CorpsId;
    }

    /// <summary>
    /// 军团指挥组件(消 D-1' 桥 #4):行动力点数(内核 Corps.ActionPoint)、回合生命
    /// 周期位(AIPrepared/AIFinished/ActionOver)、势力/军团长/番号反向引用。
    /// AP 门槛与扣减读写面化(SangoCorpsReadFace/SangoCorpsWriteFace);内核成员
    /// write-through(ReduceActionPoint 的 IsPlayer 门与 OnCorpsActionPointChange
    /// 事件位原样保持)。
    /// </summary>
    public struct SangoCorpsCommand
    {
        public int ForceId;
        public int CommanderId;
        public int Number;
        public int ActionPoint;
        public byte AIPrepared;
        public byte AIFinished;
        public byte ActionOver;
    }

    /// <summary>
    /// 军团工作计数组件:内核 Corps.jobCounter 字典的命令面切片——四型内政令中
    /// Reward 型计数在军团(Reward 门槛"本回合未褒奖"读它;Train/Searching/Recruit
    /// 是城级 dict,城组件 SangoCityJobState 已载)。
    /// </summary>
    public struct SangoCorpsJobCounters
    {
        public int RewardCounter;
    }

    /// <summary>
    /// 军团编成成员保序组件:成员城 id 按内核 citySet 扫描序(Corps.ForEachCity 同序)
    /// 保序,城/人/部队计数为扫描聚合值。定容 96(剧本城池 88,超容即类型化抛错)。
    /// </summary>
    public struct SangoCorpsMembership
    {
        public const int Capacity = 96;

        public int CityCount;
        public int PersonCount;
        public int TroopCount;
        public int GoldTotal;
        public int TroopsTotal;
        public int FoodTotal;

        [InlineArray(Capacity)]
        public struct MemberCityList
        {
#pragma warning disable CS0169
            private int _element;
#pragma warning restore CS0169
        }

        public MemberCityList CityIds;
    }

    /// <summary>
    /// 军团回合发放账本组件(对拍探针):最近一次回合发放的 AP 增量与发放后总量
    /// (内核 Corps.AddActionPoint 公式的原生计量)。测试按内核公式(君主/城市/武将
    /// 参数 × 军师系数)独立复算断言。
    /// </summary>
    public struct SangoCorpsLedger
    {
        public int LastApGranted;
        public int LastApTotal;
    }
}
