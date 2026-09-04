// M3.e 玩家科技命令内核操作层(与 SangoCityOps/SangoDiplomacyOps 同层):命令层
// (Web UI payload 解析)与重放器(SangoReplayJournal)共用的唯一实现。命令到内核的
// 映射逆向自原版科技窗口链(sango-src Project/Assets/Sango/Scripts/UI/Research/
// UITechnique.cs → Game/System/Research/TechniqueResearch.cs):
//   下单入口  城右键菜单「都市/研究技巧」(OnCityContextMenuShow:城属玩家势力且
//             == CurRunForce)→ TechniqueResearch 系统 → window_technique;
//   选科技    UITechnique.OnSelectTechniqueItem → SelectTechnique(军师自动推荐
//             ≤3 人:ForceAI.CounsellorRecommendResearch);
//   确定      UITechnique.OnSure → DoResearch → JobResearch(city, persons, tech, false):
//             GetCost=[金, 技巧点, 回合数](回合数=3-ΣneedAttr/70+counter,下限 1)→
//             扣城金/技巧点、武将入 PersonResearch 任务并离城、扣军团 AP(表 19=50)、
//             force.ResearchTechnique/ResearchLeftCounter 置位;
//   推进/完成 TechniqueResearch.OnForceTurnStart(势力回合开始):LeftCounter-- →
//             AddTechnique(效果 ActionBase 订阅 OnTroopCalculateAttribute 等静态事件,
//             全部队立即重算)→ OnForceResearchComplete + 玩家完成弹窗。
// 门槛逐条照抄 TechniqueResearch.IsValid + Technique.CanResearch(未拥有 + 前置满足)+
// JobResearch 内部金/技巧点闸;不新增平行命令、不放宽门槛;成功入 journal
// (kind=researchCommand)。

using System.Collections.Generic;
using Sango.Core;

namespace Sango.Runtime
{
    public static class SangoTechniqueOps
    {
        /// <summary>
        /// 下达研究命令。techniqueId 定目标科技;personIds 缺省(空)时按原版
        /// SelectTechnique 的军师推荐面自动选人(≤3),显式传入时要求全部落在城
        /// freePersons(原版 PersonSelectSystem 选择列表语义)。
        /// </summary>
        public static SangoTroopOpResult Execute(
            Scenario scenario, City city, int techniqueId, IReadOnlyList<int>? personIds = null)
        {
            ArgumentNullException.ThrowIfNull(scenario);
            ArgumentNullException.ThrowIfNull(city);
            if (techniqueId <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(techniqueId), "research requires a positive technique id.");
            }

            if (city.mBelongForce == null || city.mBelongCorps == null)
            {
                return SangoTroopOpResult.Fail("city_not_owned",
                    "the research city is unowned; techniques require a force and corps.");
            }

            // 玩家门与城命令同一面(原版菜单门:城属玩家势力且 == CurRunForce)。
            SangoTroopOpResult? gate = SangoPlayerTurnOps.CityCommandGate(scenario, city);
            if (gate != null)
            {
                return gate;
            }

            Technique? technique = scenario.CommonData.Techniques.Get(techniqueId);
            if (technique == null)
            {
                return SangoTroopOpResult.Fail("technique_not_found",
                    $"Techniques table has no row {techniqueId}.");
            }

            // TechniqueResearch.IsValid(系统侧门槛)+ Technique.CanResearch(未拥有 + 前置)。
            int jobId = (int)CityJobType.Research;
            if (city.mBelongForce.ResearchTechnique > 0 ||
                city.freePersons.Count == 0 ||
                city.mBelongCorps.ActionPoint < JobType.GetJobCostAP(jobId))
            {
                return SangoTroopOpResult.Fail("invalid_state",
                    "TechniqueResearch.IsValid gate rejected the order (already researching / no free persons / action points).");
            }

            if (!technique.CanResearch(city.mBelongForce))
            {
                return SangoTroopOpResult.Fail("technique_unavailable",
                    $"technique {techniqueId} is already owned or its prerequisite (needTech {technique.needTech}) is missing.");
            }

            // 执行武将:显式名单须全在 freePersons;缺省走军师推荐(原版窗口默认)。
            Person[] persons;
            if (personIds is { Count: > 0 })
            {
                Person[]? resolved = ResolveFreePersons(personIds, city);
                if (resolved == null)
                {
                    return SangoTroopOpResult.Fail("person_not_free",
                        "research requires personIds of persons currently in the city freePersons list.");
                }

                persons = resolved;
            }
            else
            {
                Person[]? recommended = ForceAI.CounsellorRecommendResearch(city.freePersons, technique);
                persons = recommended ?? Array.Empty<Person>();
                if (persons.Length == 0)
                {
                    return SangoTroopOpResult.Fail("person_not_free",
                        "the city has no free persons for the counsellor research recommendation.");
                }
            }

            int goldBefore = city.gold;
            int tpBefore = city.mBelongForce.TechniquePoint;
            int[]? cost = technique.GetCost(persons, city);
            if (cost == null)
            {
                return SangoTroopOpResult.Fail("invalid_state", "technique cost estimation returned no values.");
            }

            if (city.gold < cost[0] || city.mBelongForce.TechniquePoint < cost[1])
            {
                return SangoTroopOpResult.Fail("resources_insufficient",
                    $"research requires city gold {cost[0]} and force technique points {cost[1]} (JobResearch affordability gate).");
            }

            int[]? jobResult = TechniqueResearch.JobResearch(city, persons, technique, isTest: false);
            if (jobResult != null)
            {
                return SangoTroopOpResult.Fail("research_rejected",
                    "TechniqueResearch.JobResearch declined the order (affordability re-check inside the kernel).");
            }

            SangoCommandJournal.Record(
                SangoReplayJournal.ResearchCommandKind,
                new SangoResearchCommandArgs(techniqueId, city.Id, ArrayEmptyOrExplicit(personIds)));
            return SangoTroopOpResult.Ok(
                $"research ordered: {technique.Name} at {city.Name} for {cost[0]} gold / {cost[1]} TP, {cost[2]} turns " +
                $"(gold {goldBefore}->{city.gold}, TP {tpBefore}->{city.mBelongForce.TechniquePoint}).");
        }

        /// <summary>研究预演(原版窗口选科技/换人的即时成本显示面,isTest 路径;不改世界)。</summary>
        public static int[]? Quote(City city, int techniqueId, IReadOnlyList<int> personIds)
        {
            ArgumentNullException.ThrowIfNull(city);
            Technique? technique = Scenario.Cur?.CommonData.Techniques.Get(techniqueId);
            if (technique == null)
            {
                return null;
            }

            Person[]? persons = ResolveFreePersons(personIds, city);
            return persons == null ? null : technique.GetCost(persons, city);
        }

        static int[] ArrayEmptyOrExplicit(IReadOnlyList<int>? personIds)
        {
            if (personIds is not { Count: > 0 })
            {
                return Array.Empty<int>();
            }

            var ids = new int[personIds.Count];
            for (int i = 0; i < personIds.Count; i++)
            {
                ids[i] = personIds[i];
            }

            return ids;
        }

        static Person[]? ResolveFreePersons(IReadOnlyList<int> personIds, City city)
        {
            if (personIds.Count == 0)
            {
                return null;
            }

            var persons = new List<Person>(personIds.Count);
            foreach (int personId in personIds)
            {
                Person? person = city.freePersons.FirstOrDefault(candidate => candidate != null && candidate.Id == personId);
                if (person == null)
                {
                    return null;
                }

                persons.Add(person);
            }

            return persons.ToArray();
        }
    }
}
