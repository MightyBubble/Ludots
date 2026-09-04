// M3.a 存档补序/补值:原版存档面覆盖不到、但进入内政 AI 决策面的两类城级状态:
//   1. 有序名单(allPersons/wildPersons/invisiblePersons/allBuildings)——原版不入档,
//      回灌后按对象集扫描序重建,与活世界的到达序不同;freePersons 取位、招募/探索
//      遍历序、GetCommandBuilding 平级取先都吃这个顺序(注:SangoObjectList.ForEach
//      倒序遍历,捕获按 objects 存储正序,Apply 同序重排)。
//   2. 经济暂态(totalGainFood/Gold、人口/收入因子)——原版不入档,回灌装载线的
//      City.Init→CalculateHarvest 用装载期输入重算,与捕获时点的活值不同(捕获前刚
//      完工的建筑等已计入活值)。
//   3. 部队技能冷却(Troop.land/water/StrategySkills 的 SkillInstance.CDCount)——原版
//      技能表不入档(Troop.cs 的 [JsonProperty] 整段注释),回灌按兵种/武将重建后 CD
//      归零;活世界的冷却进度在长战役里决定技能可用性(CanBeSpell),CD 错位会让
//      战斗 AI 决策分叉(短链偶然对齐,M2 存档回归在短窗内不暴露)。按 技能名→CD
//      捕获,回灌后回放。
//   4. 太守面(城 Leader 指针 + needUpdateLeader 挂起位 + 全武将 state)——装载线
//      City.Init 无条件 UpdateNewLeader:活世界处于"选举挂起"(太守在途/已转任,
//      重选排在城回合末)时,装载期提前落定的选举会改写武将 state 与城指针,回灌
//      世界与捕获时点从此分叉(M3.a 内政 AI 活化后 AITransfrom 让该时序常态化)。
//      按捕获面回放指针与 state,挂起位经公开 NeedUpdateLeader 补真。
//   5. 势力同盟名单(Force.AllianceList)——Force 是 OptIn 且该字段无 [JsonProperty],
//      allianceSet 入档但回灌不重建成员势力的反向名单;IsAlliance/HasActiveAgreement
//      进宣战与攻击决策面(M3.d 外交接入后活跃)。按捕获序回放。
//   6. 俘虏三面(M3.g)——Troop.captiveList/City.captiveList(SangoObjectList,序列化
//      特性被上游注释:Game/Object/Troop/Troop.cs captiveList、Game/Object/City/City.cs
//      captiveList)与 Force.BeCaptiveList(普通 List,无特性)。单挑 30% 俘将
//      (DuelSystem.CaptureGeneral)、部队溃灭俘将(Troop.OnDestroy)、入城献俘
//      (Troop.OnEnterCity)都写这三面;不入档则带俘将的存档链回灌后俘将凭空消失
//      (ReleaseCaptive/Escape 不再发生,忠诚/在野流动分叉)。按捕获序回放。
//   7. 回合内 AI 决策面(M3.g)——Force/Corps/City/Troop 的 AIPrepared/AIFinished 与
//      Force.AICommandList/City.AICommandList/Corps.AICommandQueue(委托队列不可序列化)
//      不入档。回合边界存档无害(已跑实体由序列化的 ActionOver 把门);回合中途存档
//      (原版逐帧 Run 的合法时点)回灌后 prepared 归零 → DoAI 重走 AIPrepare 把整队
//      命令重灌 → 已执行命令双跑(外交/俘虏/内政再结算一遍)。回放 = captured
//      prepared 位 + "残余命令数":经内核 AIPrepare 重建完整队列(其事件订阅面只有
//      纯入列的 ClassicsCityWorking.OnCityAIPrepare 与零订阅的 OnForceAIPrepare,重建
//      无世界副作用)后削去已执行前缀。City/Corps.jobCounter 与 Corps.ActionPoint 本就
//      [JsonProperty] 在档(内政命令门在存档边界自然成立)。
//   8. 挂起内政演出事件(M3.g)——玩家/AI 的搜索(JobSearching→CityPersonSearchingEvent)
//      与同域登庸(JobRecruitPerson→CityRecruitPersonEvent)把结算排进 RenderEvent
//      队列,下一 Run() 的 Enter 才消耗随机/落世界;队列不入档,中途存档回灌即静默
//      丢单(内政"欠跑")。回放 = 按捕获序重建事件对(city/person、person/target),
//      只认未入 Enter(IsInited=false)的实例。
// 原版 Unity 侧无 bit 级续跑约束(sango-src Game/Object/City/City.cs 上述字段均无
// [JsonProperty]);本文件按 M1.d"修原版存档 bug"先例在保存面补捕获,不改内核重建
// 逻辑——回灌完成(StartScenarioCore 之后、任何回合推进之前)统一重放捕获面。

using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ludots.Core.Persistence;
using Sango.Core;

namespace Sango.Runtime
{
    /// <summary>城级"不入档但入决策面"状态的捕获面:有序名单 + 经济暂态。</summary>
    public sealed class SangoCityPersonOrder
    {
        public Dictionary<int, int[]> AllPersons { get; init; } = new();
        public Dictionary<int, int[]> WildPersons { get; init; } = new();
        public Dictionary<int, int[]> InvisiblePersons { get; init; } = new();
        public Dictionary<int, int[]> AllBuildings { get; init; } = new();
        public Dictionary<int, int> TotalGainFood { get; init; } = new();
        public Dictionary<int, int> TotalGainGold { get; init; } = new();
        public Dictionary<int, float> PopulationIncreaseFactor { get; init; } = new();
        public Dictionary<int, float> ExtraGainFoodFactor { get; init; } = new();
        public Dictionary<int, float> ExtraGainGoldFactor { get; init; } = new();
        public Dictionary<int, float> ExtraPopulationFactor { get; init; } = new();
        public Dictionary<int, Dictionary<string, int>> TroopSkillCd { get; init; } = new();

        /// <summary>太守指针(cityId → person id,0 = 空):装载线 City.Init 无条件重选太守,
        /// 活世界"选举挂起"(needUpdateLeader)时点的指针与选举结果都还未落定,按捕获面回放。</summary>
        public Dictionary<int, int> LeaderPerson { get; init; } = new();

        /// <summary>太守选举挂起位(cityId → 捕获时 needUpdateLeader):活世界的重选发生在
        /// 下一回合末,回灌世界的装载期重选必须撤回,挂起位补真(经公开 NeedUpdateLeader)。</summary>
        public Dictionary<int, bool> LeaderElectionPending { get; init; } = new();

        /// <summary>全武将 state(personId → state):装载期选举会改写 JSON 载入的 state
        /// (SetStateLeader),按捕获面整体回放——存档边界上的"捕获时点 state"即续跑真源。</summary>
        public Dictionary<int, int> PersonStates { get; init; } = new();

        /// <summary>城 AI 任务面(cityId → TroopMissionType 枚举值):CurActiveTroop/
        /// TroopMissionType/TroopMissionTargetId 是 internal 运行态(原版不入档),
        /// 回灌归零后 AIAttack/AIRecruitPerson 会重新开局一套任务(出征/征兵连锁),
        /// 与活世界的中途任务态分叉——按捕获面回放。</summary>
        public Dictionary<int, int> CityTroopMissionType { get; init; } = new();
        public Dictionary<int, int> CityTroopMissionTarget { get; init; } = new();

        /// <summary>城 AI 正在泵的部队(cityId → troopId,0 = 无)。</summary>
        public Dictionary<int, int> CityCurActiveTroop { get; init; } = new();

        /// <summary>势力存活(forceId → IsAlive):Force.IsAlive 的私有 isAlive 背书无
        /// [JsonProperty](基类属性被 override 遮蔽),回灌后灭亡势力复活会重新进回合
        /// 队列行动——按捕获面回写(经公开 setter)。</summary>
        public Dictionary<int, bool> ForceIsAlive { get; init; } = new();

        /// <summary>势力同盟名单(forceId → allianceId[]):Force.AllianceList 无 [JsonProperty]
        /// (Force 是 OptIn),allianceSet 本身入档但回灌不重建成员势力的反向名单——
        /// IsAlliance/HasActiveAgreement 读该名单(停战/同盟判定进宣战与攻击 AI 决策面),
        /// 缺席即"回灌世界无同盟"与活世界分叉。按捕获序回放(镜像序,同 allPersons 先例)。</summary>
        public Dictionary<int, int[]> ForceAllianceList { get; init; } = new();

        /// <summary>势力战力(forceId → FightPower):Force.OnForceTurnStart 清零、回合内按城
        /// 累计,不入档;送礼关系增益(DiplomacyActionSendGift 的 powerRatio)与 AI 外交
        /// 目标评分读它。玩家门(=玩家势力回合中途)捕获回灌后未跑的势力保持 0,与活世界
        /// 的中途累计值分叉——按捕获面回写。</summary>
        public Dictionary<int, int> ForceFightPower { get; init; } = new();

        /// <summary>部队俘虏名单(troopId → personId[],存储序):Troop.captiveList 不入档
        /// (特性被上游注释),回灌后空名单让单挑/溃灭俘将在存档链凭空获释——按捕获序回放。</summary>
        public Dictionary<int, int[]> TroopCaptives { get; init; } = new();

        /// <summary>城内俘虏名单(cityId → personId[],存储序):同 TroopCaptives(City.captiveList)。</summary>
        public Dictionary<int, int[]> CityCaptives { get; init; } = new();

        /// <summary>势力被俘名单(forceId → personId[],List 序):Force.BeCaptiveList 无特性,
        /// 是俘将的势力侧反向登记(OnFall 清算读它)——按捕获序回放。</summary>
        public Dictionary<int, int[]> ForceBeCaptives { get; init; } = new();

        /// <summary>回合内 AI 决策面(id → [prepared, finished, 残余命令数]):prepared 实体
        /// 经内核 AIPrepare 重建队列后削去已执行前缀,双跑即消;见文件头第 7 条。</summary>
        public Dictionary<int, int[]> ForceAiProgress { get; init; } = new();
        public Dictionary<int, int[]> CorpsAiProgress { get; init; } = new();
        public Dictionary<int, int[]> CityAiProgress { get; init; } = new();
        public Dictionary<int, int[]> TroopAiProgress { get; init; } = new();

        /// <summary>挂起内政演出事件(捕获序):搜索(cityId+personId)与同域登庸
        /// (personId+targetPersonId),回放 = 重建事件对象回 RenderEvent 队列;
        /// 见文件头第 8 条。</summary>
        public List<PendingJobEvent> PendingJobs { get; init; } = new();

        /// <summary>一条挂起的玩法型演出事件(搜索/同域登庸),按捕获序重排进队列。</summary>
        public sealed record PendingJobEvent(string Kind, int CityId, int PersonId, int TargetPersonId);

        /// <summary>从存档 JSON 节点解析(SangoSaveParticipant 写出的同形结构)。</summary>
        public static SangoCityPersonOrder? FromJson(JsonNode? node)
        {
            if (node is not JsonObject root)
            {
                return null;
            }

            return new SangoCityPersonOrder
            {
                AllPersons = ParseMap(root["allPersons"]),
                WildPersons = ParseMap(root["wildPersons"]),
                InvisiblePersons = ParseMap(root["invisiblePersons"]),
                AllBuildings = ParseMap(root["allBuildings"]),
                TotalGainFood = ParseIntMap(root["totalGainFood"]),
                TotalGainGold = ParseIntMap(root["totalGainGold"]),
                PopulationIncreaseFactor = ParseFloatMap(root["populationIncreaseFactor"]),
                ExtraGainFoodFactor = ParseFloatMap(root["extraGainFoodFactor"]),
                ExtraGainGoldFactor = ParseFloatMap(root["extraGainGoldFactor"]),
                ExtraPopulationFactor = ParseFloatMap(root["extraPopulationFactor"]),
                TroopSkillCd = ParseSkillCdMap(root["troopSkillCd"]),
                LeaderPerson = ParseIntMap(root["leaderPerson"]),
                LeaderElectionPending = ParseBoolMap(root["leaderElectionPending"]),
                PersonStates = ParseIntMap(root["personStates"]),
                CityTroopMissionType = ParseIntMap(root["cityTroopMissionType"]),
                CityTroopMissionTarget = ParseIntMap(root["cityTroopMissionTarget"]),
                CityCurActiveTroop = ParseIntMap(root["cityCurActiveTroop"]),
                ForceIsAlive = ParseBoolMap(root["forceIsAlive"]),
                ForceAllianceList = ParseMap(root["forceAllianceList"]),
                ForceFightPower = ParseIntMap(root["forceFightPower"]),
                TroopCaptives = ParseMap(root["troopCaptives"]),
                CityCaptives = ParseMap(root["cityCaptives"]),
                ForceBeCaptives = ParseMap(root["forceBeCaptives"]),
                ForceAiProgress = ParseAiProgressMap(root["aiProgress"]?["force"]),
                CorpsAiProgress = ParseAiProgressMap(root["aiProgress"]?["corps"]),
                CityAiProgress = ParseAiProgressMap(root["aiProgress"]?["city"]),
                TroopAiProgress = ParseAiProgressMap(root["aiProgress"]?["troop"]),
                PendingJobs = ParsePendingJobs(root["pendingJobs"]),
            };
        }

        static Dictionary<int, bool> ParseBoolMap(JsonNode? node)
        {
            var map = new Dictionary<int, bool>();
            if (node is not JsonObject entries)
            {
                return map;
            }

            foreach (KeyValuePair<string, JsonNode?> entry in entries)
            {
                if (int.TryParse(entry.Key, out int id) && entry.Value != null)
                {
                    map[id] = (bool)entry.Value;
                }
            }

            return map;
        }

        static Dictionary<int, int> ParseIntMap(JsonNode? node)
        {
            var map = new Dictionary<int, int>();
            if (node is not JsonObject entries)
            {
                return map;
            }

            foreach (KeyValuePair<string, JsonNode?> entry in entries)
            {
                if (int.TryParse(entry.Key, out int cityId) && entry.Value != null)
                {
                    map[cityId] = (int)entry.Value;
                }
            }

            return map;
        }

        static Dictionary<int, float> ParseFloatMap(JsonNode? node)
        {
            var map = new Dictionary<int, float>();
            if (node is not JsonObject entries)
            {
                return map;
            }

            foreach (KeyValuePair<string, JsonNode?> entry in entries)
            {
                if (int.TryParse(entry.Key, out int cityId) && entry.Value != null)
                {
                    map[cityId] = (float)entry.Value;
                }
            }

            return map;
        }

        static Dictionary<int, Dictionary<string, int>> ParseSkillCdMap(JsonNode? node)
        {
            var map = new Dictionary<int, Dictionary<string, int>>();
            if (node is not JsonObject troops)
            {
                return map;
            }

            foreach (KeyValuePair<string, JsonNode?> entry in troops)
            {
                if (!int.TryParse(entry.Key, out int troopId) || entry.Value is not JsonObject skills)
                {
                    continue;
                }

                var cds = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (KeyValuePair<string, JsonNode?> skill in skills)
                {
                    if (skill.Value != null)
                    {
                        cds[skill.Key] = (int)skill.Value;
                    }
                }

                map[troopId] = cds;
            }

            return map;
        }

        static Dictionary<int, int[]> ParseMap(JsonNode? node)
        {
            var map = new Dictionary<int, int[]>();
            if (node is not JsonObject entries)
            {
                return map;
            }

            foreach (KeyValuePair<string, JsonNode?> entry in entries)
            {
                if (!int.TryParse(entry.Key, out int cityId) || entry.Value is not JsonArray ids)
                {
                    continue;
                }

                var values = new int[ids.Count];
                for (int i = 0; i < ids.Count; i++)
                {
                    values[i] = (int)ids[i]!;
                }

                map[cityId] = values;
            }

            return map;
        }

        static List<PendingJobEvent> ParsePendingJobs(JsonNode? node)
        {
            var jobs = new List<PendingJobEvent>();
            if (node is not JsonArray entries)
            {
                return jobs;
            }

            foreach (JsonNode? entry in entries)
            {
                if (entry is not JsonObject job)
                {
                    continue;
                }

                jobs.Add(new PendingJobEvent(
                    (string?)job["kind"] ?? string.Empty,
                    (int?)job["cityId"] ?? 0,
                    (int?)job["personId"] ?? 0,
                    (int?)job["targetPersonId"] ?? 0));
            }

            return jobs;
        }

        // [prepared(bool), finished(bool), remaining(int)] 混型行,统一转 int(0/1 + 计数)。
        static Dictionary<int, int[]> ParseAiProgressMap(JsonNode? node)
        {
            var map = new Dictionary<int, int[]>();
            if (node is not JsonObject entries)
            {
                return map;
            }

            foreach (KeyValuePair<string, JsonNode?> entry in entries)
            {
                if (!int.TryParse(entry.Key, out int id) || entry.Value is not JsonArray row)
                {
                    continue;
                }

                var values = new int[row.Count];
                for (int i = 0; i < row.Count; i++)
                {
                    if (row[i] is not JsonValue value)
                    {
                        throw new SaveContextException($"aiProgress row for {id} has a non-scalar element at {i}.");
                    }

                    if (value.TryGetValue<bool>(out bool flag))
                    {
                        values[i] = flag ? 1 : 0;
                    }
                    else if (value.TryGetValue<int>(out int number))
                    {
                        values[i] = number;
                    }
                    else
                    {
                        throw new SaveContextException($"aiProgress row for {id} element {i} is neither bool nor int.");
                    }
                }

                map[id] = values;
            }

            return map;
        }

        /// <summary>回灌后按捕获序重排(成员集合必须与捕获一致,不一致即回灌分叉,fail-fast)。</summary>
        public void Apply(Scenario scenario)
        {
            scenario.citySet.ForEach(city =>
            {
                if (city == null)
                {
                    return;
                }

                if (AllPersons.TryGetValue(city.Id, out int[]? personIds) && personIds != null)
                {
                    city.allPersons.Clear();
                    foreach (int personId in personIds)
                    {
                        Person? person = scenario.personSet.Get(personId);
                        if (person == null)
                        {
                            throw new SaveOrderException(
                                $"captured allPersons references person {personId} missing from the restored personSet (city {city.Id}).");
                        }

                        city.allPersons.Add(person);
                    }
                }

                ReorderPlainList(city.wildPersons, WildPersons, city, scenario, "wildPersons");
                ReorderPlainList(city.invisiblePersons, InvisiblePersons, city, scenario, "invisiblePersons");

                if (AllBuildings.TryGetValue(city.Id, out int[]? buildingIds) && buildingIds != null)
                {
                    city.allBuildings.Clear();
                    foreach (int buildingId in buildingIds)
                    {
                        Building? building = scenario.buildingSet.Get(buildingId);
                        if (building == null)
                        {
                            throw new SaveOrderException(
                                $"captured allBuildings references building {buildingId} missing from the restored buildingSet (city {city.Id}).");
                        }

                        city.allBuildings.Add(building);
                    }
                }

                // 经济暂态覆盖装载线重算值(见文件头第 2 条)。
                if (TotalGainFood.TryGetValue(city.Id, out int totalFood))
                {
                    city.totalGainFood = totalFood;
                }
                if (TotalGainGold.TryGetValue(city.Id, out int totalGold))
                {
                    city.totalGainGold = totalGold;
                }
                if (PopulationIncreaseFactor.TryGetValue(city.Id, out float popFactor))
                {
                    city.population_increase_factor = popFactor;
                }
                if (ExtraGainFoodFactor.TryGetValue(city.Id, out float extraFood))
                {
                    city.extraGainFoodFactor = extraFood;
                }
                if (ExtraGainGoldFactor.TryGetValue(city.Id, out float extraGold))
                {
                    city.extraGainGoldFactor = extraGold;
                }
                if (ExtraPopulationFactor.TryGetValue(city.Id, out float extraPop))
                {
                    city.extraPopulationFactor = extraPop;
                }
            });

            // 部队技能冷却回放(见文件头第 3 条):按技能名对位,缺名即回灌重建面与
            // 捕获面技能集不同——回灌分叉,fail-fast。
            foreach (int troopId in TroopSkillCd.Keys)
            {
                Troop? troop = scenario.troopsSet.Get(troopId);
                if (troop == null)
                {
                    throw new SaveOrderException(
                        $"captured troopSkillCd references troop {troopId} missing from the restored troopsSet.");
                }

                Dictionary<string, int> captured = TroopSkillCd[troopId];
                var rebuilt = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (SkillInstance skill in EnumerateSkills(troop))
                {
                    rebuilt[skill.Name ?? string.Empty] = skill.CDCount;
                }

                foreach (string skillName in captured.Keys)
                {
                    if (!rebuilt.TryGetValue(skillName, out _))
                    {
                        throw new SaveOrderException(
                            $"troop {troopId} rebuilt without captured skill '{skillName}'; the restore diverged from the capture.");
                    }
                }

                foreach (SkillInstance skill in EnumerateSkills(troop))
                {
                    if (captured.TryGetValue(skill.Name ?? string.Empty, out int cd))
                    {
                        skill.CDCount = cd;
                    }
                }
            }

            // 太守面回放(见文件头第 4 条):先撤装载期改写的武将 state,再回放太守指针,
            // 最后把"选举挂起"补真——活世界的重选发生在城回合末,不在装载线。
            foreach (KeyValuePair<int, int> entry in PersonStates)
            {
                Person? person = scenario.personSet.Get(entry.Key);
                if (person == null)
                {
                    throw new SaveOrderException(
                        $"captured personStates references person {entry.Key} missing from the restored personSet.");
                }

                person.state = entry.Value;
            }

            scenario.citySet.ForEach(city =>
            {
                if (city == null)
                {
                    return;
                }

                if (LeaderPerson.TryGetValue(city.Id, out int leaderId))
                {
                    Person? leader = leaderId == 0 ? null : scenario.personSet.Get(leaderId);
                    if (leaderId != 0 && leader == null)
                    {
                        throw new SaveOrderException(
                            $"captured leaderPerson references person {leaderId} missing from the restored personSet (city {city.Id}).");
                    }

                    city.Leader = leader;
                }

                if (LeaderElectionPending.TryGetValue(city.Id, out bool pending) && pending)
                {
                    city.NeedUpdateLeader();
                }

                // 城 AI 任务面回放:internal 运行态(原版不入档)按捕获时点补真。
                if (CityTroopMissionType.TryGetValue(city.Id, out int missionType))
                {
                    city.TroopMissionType = (MissionType)missionType;
                }

                if (CityTroopMissionTarget.TryGetValue(city.Id, out int missionTarget))
                {
                    city.TroopMissionTargetId = missionTarget;
                }

                if (CityCurActiveTroop.TryGetValue(city.Id, out int activeTroopId))
                {
                    if (activeTroopId == 0)
                    {
                        city.CurActiveTroop = null;
                    }
                    else
                    {
                        Troop? active = scenario.troopsSet.Get(activeTroopId);
                        if (active == null)
                        {
                            throw new SaveOrderException(
                                $"captured cityCurActiveTroop references troop {activeTroopId} missing from the restored troopsSet (city {city.Id}).");
                        }

                        city.CurActiveTroop = active;
                    }
                }
            });

            // 势力存活回写(见 ForceIsAlive):公开 setter 写回私有背书。
            foreach (KeyValuePair<int, bool> entry in ForceIsAlive)
            {
                Force? force = scenario.forceSet.Get(entry.Key);
                if (force == null)
                {
                    throw new SaveOrderException(
                        $"captured forceIsAlive references force {entry.Key} missing from the restored forceSet.");
                }

                force.IsAlive = entry.Value;
            }

            // 势力同盟名单回放(见 ForceAllianceList):allianceSet 已由 JSON 回灌,
            // 这里按捕获序重建各成员势力的反向名单。
            foreach (KeyValuePair<int, int[]> entry in ForceAllianceList)
            {
                Force? force = scenario.forceSet.Get(entry.Key);
                if (force == null)
                {
                    throw new SaveOrderException(
                        $"captured forceAllianceList references force {entry.Key} missing from the restored forceSet.");
                }

                var restored = new List<Alliance>(entry.Value.Length);
                foreach (int allianceId in entry.Value)
                {
                    Alliance? alliance = scenario.allianceSet.Get(allianceId);
                    if (alliance == null)
                    {
                        throw new SaveOrderException(
                            $"captured forceAllianceList references alliance {allianceId} missing from the restored allianceSet (force {entry.Key}).");
                    }

                    restored.Add(alliance);
                }

                force.AllianceList.Clear();
                foreach (Alliance alliance in restored)
                {
                    force.AllianceList.Add(alliance);
                }
            }

            // 势力战力回写(见 ForceFightPower):回合内累计面按捕获时点补真。
            foreach (KeyValuePair<int, int> entry in ForceFightPower)
            {
                Force? force = scenario.forceSet.Get(entry.Key);
                if (force == null)
                {
                    throw new SaveOrderException(
                        $"captured forceFightPower references force {entry.Key} missing from the restored forceSet.");
                }

                force.FightPower = entry.Value;
            }

            // 俘虏三面回放(见文件头第 6 条):名单本体不入档,回灌侧按捕获序重建;
            // 成员缺席即回灌分叉,fail-fast。
            foreach (KeyValuePair<int, int[]> entry in TroopCaptives)
            {
                Troop? troop = scenario.troopsSet.Get(entry.Key);
                if (troop == null)
                {
                    throw new SaveOrderException(
                        $"captured troopCaptives references troop {entry.Key} missing from the restored troopsSet.");
                }

                ReplayCaptiveList(troop.captiveList, entry.Value, scenario, $"troop {entry.Key}");
            }

            scenario.citySet.ForEach(city =>
            {
                if (city == null || !CityCaptives.TryGetValue(city.Id, out int[]? personIds))
                {
                    return;
                }

                ReplayCaptiveList(city.captiveList, personIds!, scenario, $"city {city.Id}");
            });

            foreach (KeyValuePair<int, int[]> entry in ForceBeCaptives)
            {
                Force? force = scenario.forceSet.Get(entry.Key);
                if (force == null)
                {
                    throw new SaveOrderException(
                        $"captured forceBeCaptives references force {entry.Key} missing from the restored forceSet.");
                }

                var restored = new List<Person>(entry.Value.Length);
                foreach (int personId in entry.Value)
                {
                    Person? person = scenario.personSet.Get(personId);
                    if (person == null)
                    {
                        throw new SaveOrderException(
                            $"captured forceBeCaptives references person {personId} missing from the restored personSet (force {entry.Key}).");
                    }

                    restored.Add(person);
                }

                force.BeCaptiveList.Clear();
                force.BeCaptiveList.AddRange(restored);
            }

            // 回合内 AI 决策面回放(见文件头第 7 条):prepared 实体重建完整队列后削去
            // 已执行前缀,prepared/finished 位按捕获补真;残余数超建队总量即回灌分叉,
            // fail-fast。Troop 无命令队列,只补进度位(其 AIPrepare 为空方法)。
            foreach (KeyValuePair<int, int[]> entry in ForceAiProgress)
            {
                Force? force = scenario.forceSet.Get(entry.Key);
                if (force == null)
                {
                    throw new SaveOrderException(
                        $"captured aiProgress references force {entry.Key} missing from the restored forceSet.");
                }

                ReplayAiProgress(force, entry.Value, force.AICommandList, scenario, $"force {entry.Key}");
            }

            foreach (KeyValuePair<int, int[]> entry in CorpsAiProgress)
            {
                Corps? corps = scenario.corpsSet.Get(entry.Key);
                if (corps == null)
                {
                    throw new SaveOrderException(
                        $"captured aiProgress references corps {entry.Key} missing from the restored corpsSet.");
                }

                ReplayAiProgress(corps, entry.Value, corps.AICommandQueue, scenario, $"corps {entry.Key}");
            }

            scenario.citySet.ForEach(city =>
            {
                if (city == null || !CityAiProgress.TryGetValue(city.Id, out int[]? progress) || progress == null)
                {
                    return;
                }

                ReplayAiProgress(city, progress, city.AICommandList, scenario, $"city {city.Id}");
            });

            scenario.troopsSet.ForEach(troop =>
            {
                if (troop == null || !TroopAiProgress.TryGetValue(troop.Id, out int[]? progress) || progress == null)
                {
                    return;
                }

                SetAiFlags(troop, progress.Length > 0 && progress[0] != 0, progress.Length > 1 && progress[1] != 0);
            });

            // 挂起内政演出事件回放(见文件头第 8 条):按捕获序重建事件对象回
            // RenderEvent 队列(装载线已 Reset,队列为空,尾部 Add 即捕获序)。
            foreach (PendingJobEvent job in PendingJobs)
            {
                Person? person = scenario.personSet.Get(job.PersonId);
                if (person == null)
                {
                    throw new SaveOrderException(
                        $"captured pendingJobs references person {job.PersonId} missing from the restored personSet.");
                }

                if (job.Kind == "search")
                {
                    City? city = scenario.citySet.Get(job.CityId);
                    if (city == null)
                    {
                        throw new SaveOrderException(
                            $"captured pendingJobs references city {job.CityId} missing from the restored citySet.");
                    }

                    var search = Sango.Render.RenderEvent.Instance.Create<Sango.Render.CityPersonSearchingEvent>();
                    search.Init(city, person);
                    Sango.Render.RenderEvent.Instance.Add(search);
                }
                else if (job.Kind == "recruit")
                {
                    Person? target = scenario.personSet.Get(job.TargetPersonId);
                    if (target == null)
                    {
                        throw new SaveOrderException(
                            $"captured pendingJobs references target person {job.TargetPersonId} missing from the restored personSet.");
                    }

                    var recruit = Sango.Render.RenderEvent.Instance.Create<Sango.Render.CityRecruitPersonEvent>();
                    recruit.Init(person, target);
                    Sango.Render.RenderEvent.Instance.Add(recruit);
                }
            }
        }

        // AIPrepare/进度位的反射入口缓存:Force/Corps 的 AIPrepare 私有、City 公开虚
        // (子类覆写按运行时型取)——重建队列走内核唯一入口,不复制命令清单(内核
        // 演进时回放自动跟随);AIPrepared/AIFinished 四类各自声明,无公共基面,同经缓存。
        static readonly Dictionary<Type, System.Reflection.MethodInfo> AIPrepareCache = new();
        static readonly Dictionary<Type, (System.Reflection.PropertyInfo Prepared, System.Reflection.PropertyInfo Finished)> ProgressPropsCache = new();

        static System.Reflection.MethodInfo AIPrepareOf(Type type)
        {
            lock (AIPrepareCache)
            {
                if (!AIPrepareCache.TryGetValue(type, out System.Reflection.MethodInfo? method))
                {
                    method = type.GetMethod("AIPrepare", System.Reflection.BindingFlags.Instance |
                            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)
                        ?? throw new InvalidOperationException($"{type.Name}.AIPrepare is missing; the aiProgress replay cannot rebuild the command queue.");
                    AIPrepareCache[type] = method;
                }

                return method;
            }
        }

        static void SetAiFlags(object entity, bool prepared, bool finished)
        {
            Type type = entity.GetType();
            (System.Reflection.PropertyInfo preparedProp, System.Reflection.PropertyInfo finishedProp) flags;
            lock (ProgressPropsCache)
            {
                if (!ProgressPropsCache.TryGetValue(type, out flags))
                {
                    flags = (type.GetProperty("AIPrepared") ?? throw new InvalidOperationException($"{type.Name}.AIPrepared is missing."),
                        type.GetProperty("AIFinished") ?? throw new InvalidOperationException($"{type.Name}.AIFinished is missing."));
                    ProgressPropsCache[type] = flags;
                }
            }

            flags.preparedProp.SetValue(entity, prepared);
            flags.finishedProp.SetValue(entity, finished);
        }

        static void ReplayAiProgress(SangoObject entity, int[] progress, object commandQueue, Scenario scenario, string owner)
        {
            bool prepared = progress.Length > 0 && progress[0] != 0;
            bool finished = progress.Length > 1 && progress[1] != 0;
            int remaining = progress.Length > 2 ? progress[2] : 0;

            if (prepared)
            {
                AIPrepareOf(entity.GetType()).Invoke(entity, new object[] { scenario });
                int total = commandQueue is System.Collections.ICollection collection ? collection.Count : 0;
                if (remaining > total)
                {
                    throw new SaveOrderException(
                        $"captured aiProgress of {owner} keeps {remaining} commands but the rebuilt queue holds {total}; the restore diverged from the capture.");
                }

                int executed = total - remaining;
                if (commandQueue is List<System.Func<Force, Scenario, bool>> forceList)
                {
                    forceList.RemoveRange(0, executed);
                }
                else if (commandQueue is List<System.Func<City, Scenario, bool>> cityList)
                {
                    cityList.RemoveRange(0, executed);
                }
                else if (commandQueue is Queue<System.Func<Corps, Scenario, bool>> corpsQueue)
                {
                    for (int i = 0; i < executed; i++)
                    {
                        corpsQueue.Dequeue();
                    }
                }
            }

            SetAiFlags(entity, prepared, finished);
        }

        static void ReplayCaptiveList(SangoObjectList<Person> list, int[] personIds, Scenario scenario, string owner)
        {
            list.Clear();
            foreach (int personId in personIds)
            {
                Person? person = scenario.personSet.Get(personId);
                if (person == null)
                {
                    throw new SaveOrderException(
                        $"captured captive list of {owner} references person {personId} missing from the restored personSet.");
                }

                list.Add(person);
            }
        }

        static IEnumerable<SkillInstance> EnumerateSkills(Troop troop)
        {
            foreach (SkillInstance? skill in troop.landSkills)
            {
                if (skill != null)
                {
                    yield return skill;
                }
            }

            foreach (SkillInstance? skill in troop.waterSkills)
            {
                if (skill != null)
                {
                    yield return skill;
                }
            }

            foreach (SkillInstance? skill in troop.StrategySkills)
            {
                if (skill != null)
                {
                    yield return skill;
                }
            }
        }

        static void ReorderPlainList(
            List<Person> list, Dictionary<int, int[]> captured, City city, Scenario scenario, string name)
        {
            if (!captured.TryGetValue(city.Id, out int[]? ids) || ids == null)
            {
                return;
            }

            var restored = new List<Person>(ids.Length);
            foreach (int personId in ids)
            {
                Person? person = scenario.personSet.Get(personId);
                if (person == null)
                {
                    throw new SaveOrderException(
                        $"captured {name} references person {personId} missing from the restored personSet (city {city.Id}).");
                }

                restored.Add(person);
            }

            list.Clear();
            list.AddRange(restored);
        }
    }

    /// <summary>回灌序与回灌世界成员不一致:捕获链与回灌链已在存档边界分叉。</summary>
    public sealed class SaveOrderException : InvalidOperationException
    {
        public SaveOrderException(string message) : base(message) { }
    }
}
