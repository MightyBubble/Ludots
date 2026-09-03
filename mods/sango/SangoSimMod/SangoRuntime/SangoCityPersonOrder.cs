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
// 原版 Unity 侧无 bit 级续跑约束(sango-src Game/Object/City/City.cs 上述字段均无
// [JsonProperty]);本文件按 M1.d"修原版存档 bug"先例在保存面补捕获,不改内核重建
// 逻辑——回灌完成(StartScenarioCore 之后、任何回合推进之前)统一重放捕获面。

using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
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
