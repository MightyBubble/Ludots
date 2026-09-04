// D-1' 过渡桥:武将/势力/军团仍 PONO 期间,原生城系统对旧内核对象面的全部访问收敛于本文件。
// 非 facade:不路由命令、不持状态、不做语义判断——只把"跨域对象访问"从原生系统里
// 拎出来集中可见,后续波(武将域 D-2'、军团/势力域)逐条消亡。
//
// ===== 跨域访问登记表(消亡前唯一合法入口;新增访问必须在此登记) =====
// | # | 域 | 访问面 | 用途 | 消亡波 |
// |---|----|--------|------|--------|
// | 1 | 武将 Person | loyalty/state/mCurrentCity/mBelongForce/mTroop/IsFree 读 | 内政门槛与俸给公式 | D-2' |
// | 2 | 武将 Person | Politics/Command/BaseTrainTroopAbility/Official.cost 读 | 训练/探索/俸给公式 | D-2' |
// | 3 | 武将 Person | merit+=/GainExp/ActionOver/loyalty+=/SetMission 写 | 四型内政令结算(照内核 Job* 原语义) | D-2' |
// | 4 | 军团 Corps | ActionPoint/ReduceActionPoint/GetJobCounter/AddJobCounter | 内政令 AP 门槛与军团级 jobCounter | 军团域波 |
// | 5 | 势力 Force | GainTechniquePoint/mGovernor/IsPlayer 读 | 内政令功勋点/俸给门槛 | 势力域波 |
// | 6 | 城 City(PONO) | gold/food/population/durability/morale/MaxMorale/troops/woundedTroops 读 | 内核保留面(combat/OnForceTurnStart)对账与公式输入 | 城域终局 |
// | 7 | 城 City(PONO) | gold/food/totalGainGold/totalGainFood/population_increase_factor 字段写 | 原生结算面 write-through(内核下游读内核值,见 SangoCityNativeRuntime 文件头) | 城域终局 |
// | 8 | 城 City(PONO) | freePersons/wildPersons/invisiblePersons/captiveList/allPersons/allBuildings/jobCounter/AIPrepared/AIFinished/ActionOver 读 | 有序名单/job 状态镜像面 | 城域终局 |
// | 9 | 城 City(PONO) | freePersons.Remove/GetJobCounter 读, morale/jobCounter 写 | 四型内政令结算的名单与计数面 | 城域终局 |
// | 10 | 事件面 GameEvent | OnCityGainGoldHarvest/OnCityGainFoodHarvest/OnCityCalculateFoodCost/OnCityCheckJobCost/OnCityJobResult/OnCityJobGainTechniquePoint/OnCityJobSearchingWild 触发 | 原生结算与命令照内核同位触发(城附属 Action 修改器继续生效) | 城域终局 |
// | 11 | 演出队列 RenderEvent | CityPersonSearchingEvent/CityRecruitPersonEvent 入列 | 探索/登庸令的次回合结算排程(内核演出事件机制) | 演出面迁移波 |
// | 12 | 公式库 GameUtility/GameFormula | Method_TrainTroops/InitJobFeature/ClearJobFeature/PersonEscapeProbablility_InCity | 纯公式/静态上下文(非对象状态) | 随各域 |
// | 13 | 内核 CityAI | 命令函数(AITrainTroop 等)入列 | 内政 AI 的执行面(决策面已原生,见 SangoCityAIPrepare) | 内政 job 溶解波 |
// | 14 | 随机 GameRandom | Random(base,floatP)/Chance/Range | 原生结算与命令在内核同位取随机(共享流,对拍根基) | 随内核各域 |
//
// 事件面交换(内核 handler 位置保持替换,不改内核代码)见 SangoCityEventSwap:
// ClassicsCityWorking 的 OnCityMonthStart/OnCitySeasonStart/OnCityCalculateHarvest/
// OnCityAIPrepare 与 BuildingWorking 的 OnCityCalculateHarvest(经典模式复合收获链)
// 被原生实现按同一调用位替换;研究域(TechniqueResearch)等他域订阅不动。

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

        // ---- #4:军团面 ----

        public static int CorpsActionPoint(City city) => city.mBelongCorps?.ActionPoint ?? 0;

        public static void CorpsReduceActionPoint(City city, int amount) =>
            city.mBelongCorps?.ReduceActionPoint(amount);

        public static int CorpsJobCounter(City city, int jobId) => city.mBelongCorps?.GetJobCounter(jobId) ?? 0;

        public static void CorpsAddJobCounter(City city, int jobId)
        {
            city.mBelongCorps?.AddJobCounter(jobId);
        }

        // ---- #5:势力面 ----

        public static void ForceGainTechniquePoint(City city, int points) =>
            city.mBelongForce?.GainTechniquePoint(points);

        public static bool IsForceGovernor(City city, Person person) =>
            ReferenceEquals(person, city.mBelongForce?.mGovernor);
    }
}
