// D-2' 武将域原生实现:数据组件 + GAS 属性面。
// 组件合同(同 D-1' 城域):全部 unmanaged struct,经引擎持久化发现链
// (LudotsCorePersistenceFormatters.AddAutoDiscoveredUnmanagedFormatters)自动获得
// world.bin 原生序列化,不建平行持久化。数值(统/武/智/政/忠诚)不入组件——走
// GAS attribute(sango.person.*,注册进引擎 AttributeRegistry 唯一表,值写入走
// AttributeMutationOps.SetBase 正式通道;SetBase 自带同值幂等省略,批量同步
// 天然节流)。状态/归属反向引用/任务态/履历为组件(城名单引用已在城组件,
// 武将侧只保留反向引用,不重复城序)。
// 容量合同:内核 personSet 上限 851(id 0 空位,实存 850)——物化面按同一上限
// 校验,越界类型化抛错,不静默截断。

using System.Runtime.CompilerServices;

namespace Sango.Runtime
{
    /// <summary>
    /// 武将 GAS 属性注册面(同 SangoCityAttributes 模式:引擎 AttributeRegistry
    /// 唯一表,SangoSimModEntry.OnLoad 走 context 面,运行时/测试侧幂等注册)。
    /// 值语义 = 内核 Person.Command/Strength/Intelligence/Politics 的**计算值**
    /// (base + 装备加成,收获因子/训练能力读的同一面)与 Person.loyalty。
    /// </summary>
    public static class SangoPersonAttributes
    {
        public const string Command = "sango.person.command";
        public const string Strength = "sango.person.strength";
        public const string Intelligence = "sango.person.intelligence";
        public const string Politics = "sango.person.politics";
        public const string Loyalty = "sango.person.loyalty";

        static int _command = Ludots.Core.Gameplay.GAS.Registry.AttributeRegistry.InvalidId;
        static int _strength = Ludots.Core.Gameplay.GAS.Registry.AttributeRegistry.InvalidId;
        static int _intelligence = Ludots.Core.Gameplay.GAS.Registry.AttributeRegistry.InvalidId;
        static int _politics = Ludots.Core.Gameplay.GAS.Registry.AttributeRegistry.InvalidId;
        static int _loyalty = Ludots.Core.Gameplay.GAS.Registry.AttributeRegistry.InvalidId;

        public static int CommandId => _command;
        public static int StrengthId => _strength;
        public static int IntelligenceId => _intelligence;
        public static int PoliticsId => _politics;
        public static int LoyaltyId => _loyalty;

        /// <summary>幂等注册(重复名返回既有 id;引擎表 Register 对重名显式抛错,故先查后注)。</summary>
        public static void EnsureRegistered()
        {
            _command = Ensure(Command);
            _strength = Ensure(Strength);
            _intelligence = Ensure(Intelligence);
            _politics = Ensure(Politics);
            _loyalty = Ensure(Loyalty);
        }

        static int Ensure(string name)
        {
            int id = Ludots.Core.Gameplay.GAS.Registry.AttributeRegistry.GetId(name);
            return id != Ludots.Core.Gameplay.GAS.Registry.AttributeRegistry.InvalidId
                ? id
                : Ludots.Core.Gameplay.GAS.Registry.AttributeRegistry.Register(name);
        }
    }

    /// <summary>武将身份:内核武将 id(Person.Id,武将实体与内核 Person PONO 的对账键)。</summary>
    public struct SangoPersonIdentity
    {
        public int PersonId;
    }

    /// <summary>
    /// 武将归属反向引用组件:势力/军团/所属城/所在城/所属部队(城名单的正向引用
    /// 已在城组件 SangoCityRoster;本组件是武将侧反向引用,id 0 = 无)。值同步自
    /// 内核活引用(mBelongForce 等)——内核序列化 int 字段(BelongForce 等)仅在
    /// 存档时点回写,活世界是脏值(同 WorldDigest 武将行的在案语义)。
    /// </summary>
    public struct SangoPersonMembership
    {
        public int BelongForceId;
        public int BelongCorpsId;
        public int BelongCityId;
        public int CurrentCityId;
        public int BelongTroopId;
    }

    /// <summary>
    /// 武将状态组件:身分(PersonStateType:在野/俘虏/隐形/死亡/官职面)、行动结束位、
    /// 在野停留计数(内核 Person.OnTurnEnd 在野移动面的输入,内核保留结算、组件镜像)。
    /// </summary>
    public struct SangoPersonStatus
    {
        public byte State;
        public byte ActionOver;
        public int StayTurnCount;
        public int WildTurnCount;
    }

    /// <summary>
    /// 武将履历组件:功勋/经验/等级/官职(俸给面读 OfficialCost——消 D-1' 桥 #2 的
    /// 组件源)。Exp 为内核 Person.Exp 的镜像(含内核 GainExp 升级回绕语义的原样值)。
    /// </summary>
    public struct SangoPersonCareer
    {
        public int Merit;
        public int Exp;
        public int LevelId;
        public int OfficialId;
        public int OfficialCost;
    }

    /// <summary>武将任务态组件(Person.mission* 七字段的组件面;SetMission 写入走原生写面)。</summary>
    public struct SangoPersonMission
    {
        public int MissionType;
        public int MissionTarget;
        public int MissionCounter;
        public int MissionParams1;
        public int MissionParams2;
        public int MissionParams3;
        public int MissionParams4;
    }

    /// <summary>
    /// 武将写面账本组件(对拍探针):训练/褒奖/任务原生写面的最近一次计量与累计计数,
    /// 测试按内核公式(JobType.GetJobMeritGain / 忠诚+10 / GainExp 升级链)独立复算断言。
    /// LastExpDelta 是应用后观测差(内核升级回绕语义下可为负)。
    /// </summary>
    public struct SangoPersonLedger
    {
        public int LastMeritGain;
        public int LastExpDelta;
        public int LastLoyaltyDelta;
        public int TrainCount;
        public int RewardCount;
        public int MissionCount;
    }

    /// <summary>定容武将 id 容器(按内核 personSet 容量语义;InlineArray,unmanaged)。</summary>
    [InlineArray(Capacity)]
    public struct SangoPersonIdList
    {
        public const int Capacity = 851;

#pragma warning disable CS0169 // 字段经 InlineArray 展开访问,不直接引用
        private int _element;
#pragma warning restore CS0169
    }
}
