// D-1' 城域原生实现:数据组件 + GAS 属性面。
// 组件合同:全部 unmanaged struct(InlineArray 保序、定容、打满即抛错不截断),
// 经引擎持久化发现链(LudotsCorePersistenceFormatters.AddAutoDiscoveredUnmanagedFormatters,
// 候选集=AppDomain 全程序集)自动获得 world.bin 原生序列化,不建平行持久化。
// 数值(金/粮/人口/耐久)不入组件——走 GAS attribute(sango.city.*,见 SangoCityAttributes,
// 注册进引擎 AttributeRegistry 唯一表,值写入走 AttributeMutationOps.SetBase 正式通道)。

using System.Runtime.CompilerServices;

namespace Sango.Runtime
{
    /// <summary>
    /// 城 GAS 属性注册面:经引擎 AttributeRegistry 唯一表(与 IModContext.Registries.
    /// RegisterAttribute 同一落地,Mod 装载侧由 SangoSimModEntry.OnLoad 走 context 面;
    /// 运行时/测试侧由 EnsureRegistered 幂等注册)。模板 sango.city 的 AttributeBuffer
    /// 数据按名解析到同一批 id。
    /// </summary>
    public static class SangoCityAttributes
    {
        public const string Gold = "sango.city.gold";
        public const string Food = "sango.city.food";
        public const string Population = "sango.city.population";
        public const string Durability = "sango.city.durability";

        static int _gold = Ludots.Core.Gameplay.GAS.Registry.AttributeRegistry.InvalidId;
        static int _food = Ludots.Core.Gameplay.GAS.Registry.AttributeRegistry.InvalidId;
        static int _population = Ludots.Core.Gameplay.GAS.Registry.AttributeRegistry.InvalidId;
        static int _durability = Ludots.Core.Gameplay.GAS.Registry.AttributeRegistry.InvalidId;

        public static int GoldId => _gold;
        public static int FoodId => _food;
        public static int PopulationId => _population;
        public static int DurabilityId => _durability;

        /// <summary>幂等注册(重复名返回既有 id;引擎表 Register 对重名显式抛错,故先查后注)。</summary>
        public static void EnsureRegistered()
        {
            _gold = Ensure(Gold);
            _food = Ensure(Food);
            _population = Ensure(Population);
            _durability = Ensure(Durability);
        }

        static int Ensure(string name)
        {
            int id = Ludots.Core.Gameplay.GAS.Registry.AttributeRegistry.GetId(name);
            return id != Ludots.Core.Gameplay.GAS.Registry.AttributeRegistry.InvalidId
                ? id
                : Ludots.Core.Gameplay.GAS.Registry.AttributeRegistry.Register(name);
        }
    }

    /// <summary>城身份:内核城 id(City.Id,城实体与内核 City PONO 的对账键)。</summary>
    public struct SangoCityIdentity
    {
        public int CityId;
    }

    /// <summary>定容有序 int 名单(保序组件的底层容器;InlineArray,unmanaged)。</summary>
    [InlineArray(Capacity)]
    public struct SangoCityIdList
    {
        public const int Capacity = 192;

#pragma warning disable CS0169 // 字段经 InlineArray 展开访问,不直接引用
        private int _element;
#pragma warning restore CS0169
    }

    /// <summary>
    /// 城有序名单组件:在野/俘虏/隐形武将与建筑,按内核到达序保序(与
    /// SangoCityPersonOrder 捕获面同源同序)。名单指纹(digest)对四段有序 id
    /// 做 FNV-1a,空段参与常量前缀,顺序变化即变指纹。
    /// </summary>
    public struct SangoCityRoster
    {
        public int WildCount;
        public int CaptiveCount;
        public int InvisibleCount;
        public int BuildingCount;
        public SangoCityIdList WildPersonIds;
        public SangoCityIdList CaptivePersonIds;
        public SangoCityIdList InvisiblePersonIds;
        public SangoCityIdList BuildingIds;

        public static int RosterFingerprint(in SangoCityRoster roster)
        {
            unchecked
            {
                uint hash = 2166136261;
                hash = HashIds(roster.WildPersonIds, roster.WildCount, hash, 0xA1);
                hash = HashIds(roster.CaptivePersonIds, roster.CaptiveCount, hash, 0xC2);
                hash = HashIds(roster.InvisiblePersonIds, roster.InvisibleCount, hash, 0xE3);
                hash = HashIds(roster.BuildingIds, roster.BuildingCount, hash, 0xB4);
                return (int)hash;
            }
        }

        static uint HashIds(in SangoCityIdList ids, int count, uint hash, uint sectionTag)
        {
            unchecked
            {
                hash = (hash ^ sectionTag) * 16777619;
                hash = (hash ^ (uint)count) * 16777619;
                for (int i = 0; i < count; i++)
                {
                    hash = (hash ^ (uint)ids[i]) * 16777619;
                }

                return hash;
            }
        }
    }

    /// <summary>
    /// 内政 job 状态组件:城级 jobCounter(内核 City.jobCounter 字典的命令面切片——
    /// 四型命令 id 的计数值)与回合生命周期位(AIPrepared/AIFinished/ActionOver)。
    /// 军团级 jobCounter(Reward 型)与军团 ActionPoint 属军团域,不入本组件(D-2' 后续)。
    /// </summary>
    public struct SangoCityJobState
    {
        public int TrainCounter;
        public int SearchingCounter;
        public int RecruitCounter;
        public int RewardCounter;
        public byte AIPrepared;
        public byte AIFinished;
        public byte ActionOver;
    }

    /// <summary>
    /// 城经济组件:复合收获链(ClassicsCityWorking 公式 + BuildingWorking 经典模式
    /// 覆盖,按内核订阅序复合)的收入总额与人口因子,以及原生结算面的最近一次
    /// 计量(对拍探针:跨运行 write-through digest + 本组件计值双证明;
    /// Base 字段记录取随机时点的收获基数——月中名单/建筑变动会让回合末总额
    /// 与取数基数不同,账本以取数时点为准)。
    /// </summary>
    public struct SangoCityEconomy
    {
        public int TotalGainGold;
        public int TotalGainFood;
        public float PopulationIncreaseFactor;
        public int LastIncomeGold;
        public int LastIncomeDrawGold;
        public int LastIncomeBaseGold;
        public int LastSalaryGold;
        public int LastHarvestFood;
        public int LastHarvestDrawFood;
        public int LastHarvestBaseFood;
        public int LastFoodCost;
        public int FallCount;
    }

    /// <summary>AI 决策计划组件:城 AI 命令序(原生 OnCityAIPrepare 决策面的产物,
    /// 命令种类按入列序记录;执行面仍是内核 CityAI 函数,见 SangoLegacyBridge 登记)。</summary>
    public struct SangoCityAIPlan
    {
        public const int Capacity = 32;

        public int Count;
        public byte BorderCity;
        public byte AIPreparedThisTurn;

        [InlineArray(Capacity)]
        public struct PlanKinds
        {
#pragma warning disable CS0169
            private byte _element;
#pragma warning restore CS0169
        }

        public PlanKinds Kinds;
    }

    /// <summary>AI 计划条目种类(与内核 CityAI 命令函数一一对应;Other=研究等他域追加)。</summary>
    public enum SangoCityAICommandKind : byte
    {
        None = 0,
        RewardPerson = 1,
        Searching = 2,
        RecruitPerson = 3,
        Attack = 4,
        TradeFood = 5,
        Security = 6,
        TrainTroop = 7,
        RecruitTroop = 8,
        Interior = 9,
        CreateItems = 10,
        Transform = 11,
        Other = 12,
    }
}
