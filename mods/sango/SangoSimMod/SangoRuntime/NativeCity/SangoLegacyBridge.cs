// D-1' 过渡桥:原生城系统对旧内核对象面的全部访问收敛于本文件;D-2' 起武将域
// 读写面组件化(消 #1/#2/#3),D-3' 起部队/军团域读写面组件化(消 #4),
// 本文件同时登记部队/军团域残留面。
// 非 facade:不路由命令、不持状态、不做语义判断——只把"跨域对象访问"从原生系统里
// 拎出来集中可见,后续波(势力域、内核终局 D-5')逐条消亡。
//
// ===== 跨域访问登记表(消亡前唯一合法入口;新增访问必须在此登记) =====
// | # | 域 | 访问面 | 用途 | 消亡波 |
// |---|----|--------|------|--------|
// | 1 | 武将 Person | ~~loyalty/state/mTroop 读~~ | D-2' 已消:读写面经 SangoPersonReadFace/SangoPersonWriteFace 组件化(SangoPersonNativeRuntime) | 消亡 |
// | 2 | 武将 Person | ~~Politics/Command/BaseTrainTroopAbility/Official.cost 读~~ | D-2' 已消:五维走 GAS 属性(sango.person.*)、官职费走 SangoPersonCareer 组件;魅力(AttributeType 4)暂读内核计算值,归后续属性面扩波 | 基本消亡 |
// | 3 | 武将 Person | ~~merit+=/GainExp/ActionOver/loyalty+=/SetMission 写~~ | D-2' 已消:城域 job 结算体的武将写入段全部经 SangoPersonWriteFace(组件+内核 write-through,语句序保持内核原序) | 消亡 |
// | 4 | 军团 Corps | ~~ActionPoint/ReduceActionPoint/GetJobCounter/AddJobCounter~~ | D-3' 已消:读写面经 SangoCorpsReadFace/SangoCorpsWriteFace 组件化(SangoTroopNativeRuntime;Reward 切片外 jobCounter 暂读内核字典,见读面注) | 基本消亡 |
// | 4a | 部队 Troop | missionType/missionTarget/missionParams/missionTargetCell 读;setMission/ClearMission/NeedPrepareMission 写 | D-3' 任务态组件化(SangoTroopMission + SangoTroopWriteFace):原生读写面不再触碰 TroopMissionBehaviour 懒重建链;内核行为体内部仍按其原语义运行(P2 同款保留面) | 随内核终局(D-5') |
// | 4b | 部队 Troop | troops/morale/food/foodCost/liveDays/captiveList 读 | D-3' GAS 属性(sango.troop.*)+ 组件(SangoTroopLedger/SangoTroopCaptives);战斗解算本体(ChangeTroops/ChangeMorale/技能链)仍是 D-4' 的面,本波承载域按读缝同步 | D-4'(解算全 GAS) |
// | 5 | 势力 Force | GainTechniquePoint/mGovernor/IsPlayer 读 | 内政令功勋点/俸给门槛 | 势力域波 |
// | 6 | 城 City(PONO) | gold/food/population/durability/morale/MaxMorale/troops/woundedTroops 读 | 内核保留面(combat/OnForceTurnStart)对账与公式输入 | 城域终局 |
// | 7 | 城 City(PONO) | gold/food/totalGainGold/totalGainFood/population_increase_factor 字段写 | 原生结算面 write-through(内核下游读内核值,见 SangoCityNativeRuntime 文件头) | 城域终局 |
// | 8 | 城 City(PONO) | freePersons/wildPersons/invisiblePersons/captiveList/allPersons/allBuildings/jobCounter/AIPrepared/AIFinished/ActionOver 读 | 有序名单/job 状态镜像面 | 城域终局 |
// | 9 | 城 City(PONO) | freePersons.Remove/GetJobCounter 读, morale/jobCounter 写 | 四型内政令结算的名单与计数面 | 城域终局 |
// | 10 | 事件面 GameEvent | OnCityGainGoldHarvest/OnCityGainFoodHarvest/OnCityCalculateFoodCost/OnCityCheckJobCost/OnCityJobResult/OnCityJobGainTechniquePoint/OnCityJobSearchingWild 触发 | 原生结算与命令照内核同位触发(城附属 Action 修改器继续生效) | 城域终局 |
// | 11 | 演出队列 RenderEvent | CityPersonSearchingEvent/CityRecruitPersonEvent 入列 | 探索/登庸令的次回合结算排程(内核演出事件机制) | 演出面迁移波 |
// | 12 | 公式库 GameUtility/GameFormula | Method_TrainTroops/InitJobFeature/ClearJobFeature/PersonEscapeProbablility_InCity | 纯公式/静态上下文(非对象状态) | 随各域 |
// | 13 | 内核 CityAI | 命令函数(AITrainTroop 等)入列;执行体读 person.loyalty(AIRewardPerson)/freePersons | 内政 AI 的执行面(决策面已原生,见 SangoCityAIPrepare) | 内政 job 溶解波 |
// | 14 | 随机 GameRandom | Random(base,floatP)/Chance/Range | 原生结算与命令在内核同位取随机(共享流,对拍根基) | 随内核各域 |
// | P1 | 武将 Person | PONO 引用仅作 id 键传递(公式/门槛入参 person → person.Id 查组件) | 原生读写面的键合同(值与副作用全部组件/内核 write-through,不读 PONO 字段) | D-5'(内核退场) |
// | P2 | 武将/部队/军团 | 回合结算虚方法链保留面:换季掉忠 Force.OnSeasonStart、登场 Person.OnTurnStart、俘虏逃逸 City/Troop.OnForceTurnEnd、部队回合体 Troop.OnForceTurnStart(耗粮/断粮士气/CD 推进)、军团回合体 Corps.OnForceTurnStart(jobCounter 清零/AP 发放)(内核经 Scenario.Run→Force 直调,无事件订阅面可交换——闸门在案结论) | 内核保留执行,原生侧镜像对账(势力回合边界 + 读缝同步)+ 账本/直方图探针 | 城域/内核终局(D-5') |
// | P3 | 武将 Person | 招揽/搜索次回合结算(CityRecruitPersonEvent/CityPersonSearchingEvent.Enter:JobRecruitPerson/DoJobSearching 写 loyalty=80/state/membership) | 演出事件驱动的写面(内核保留;组件经回合边界与生命周期事件同步收敛) | 演出面迁移波(同 #11) |
// | P4 | 武将 Person | GainExp 升级链(Level 对象链 + OnPersonLevelUp)/SetMission/ActionOver setter 经内核成员调用 | 原生写面的副作用保真载体(事件位/等级链不变;值同步进组件) | D-5'/CommonData 溶解 |
//
// 事件面交换(内核 handler 位置保持替换,不改内核代码)见 SangoCityEventSwap:
// ClassicsCityWorking 的 OnCityMonthStart/OnCitySeasonStart/OnCityCalculateHarvest/
// OnCityAIPrepare 与 BuildingWorking 的 OnCityCalculateHarvest(经典模式复合收获链)
// 被原生实现按同一调用位替换;研究域(TechniqueResearch)等他域订阅不动。
// D-2' 武将域内核事件订阅(OnPerson* 六面/回合与日界/命令漏斗)全部为追加式——
// 内核武将结算体无既有订阅者可替换(见 P2),非权威期一律 no-op(通用合同)。

using System;
using Sango.Core;

namespace Sango.Runtime
{
    /// <summary>过渡桥:显式包装 + 登记(见文件头表),不隐藏任何跨域耦合。</summary>
    public static class SangoLegacyBridge
    {
        // ---- #6/#7/#8/#9:城 PONO 读写面(内核保留面的对账与 write-through) ----

        public static int ReadGold(City city) => city.gold;

        public static void WriteGold(City city, int value) => city.gold = value;

        public static int ReadFood(City city) => city.food;

        public static void WriteFood(City city, int value) => city.food = value;

        public static int ReadPopulation(City city) => city.population;

        public static int ReadDurability(City city) => city.durability;

        public static int ReadMorale(City city) => city.morale;

        public static int ReadTroops(City city) => city.troops;

        public static int ReadWoundedTroops(City city) => city.woundedTroops;

        public static int ReadForceId(City city) => city.mBelongForce?.Id ?? 0;

        public static bool IsPlayerCity(City city) => city.IsPlayer;

        // ---- #4:军团面(D-3' 已消:读写面化,见 SangoTroopNativeRuntime) ----
        // ---- #5:势力面 ----

        public static void ForceGainTechniquePoint(City city, int points) =>
            city.mBelongForce?.GainTechniquePoint(points);

        public static bool IsForceGovernor(City city, Person person) =>
            ReferenceEquals(person, city.mBelongForce?.mGovernor);
    }
}
