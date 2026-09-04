// 存档域 sango.sim(M1.d):世界态经 TK fork 序列化为内存 JSON,包进 Ludots 存档容器的
// domains 节点,由 SaveContainerCodec 统一落盘——从根上绕开原版 Scenario.Save 的裸
// System.IO.File.CreateText,不需要给 VFS 网关加写后端。
// 捕获面 = 原版 Scenario.Save 的语义截取:
//   · prepareList 七个对象池先过 OnScenarioSave(活引用 m 字段同步回 Id 字段);
//   · CommonData 不入档(NullValueHandling.Ignore + 置空),回灌侧从数据表整表重载,
//     Id 引用经 Id2ObjConverter 延迟绑定的 OnScenarioPrepare 回放重建;
//   · 相机态不刷新(原版从 MapRender 相机读写;headless 相机是 no-op shim,真实相机
//     归 M2 战争视口,届时再决定相机态入档面)。
// 回灌 = SangoKernelBoot.Restore:与 Boot 同一启动序列,正文换内存 JSON。

using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Ludots.Core.Modding;
using Ludots.Core.Persistence;
using Sango.Core;
using TKNewtonsoft.Json;

namespace Sango.Runtime
{
    public sealed class SangoSaveParticipant : ISaveParticipant
    {
        private readonly IVirtualFileSystem _vfs;
        private readonly string _contentModId;
        private readonly string _scenarioAssetPath;

        public SangoSaveParticipant(IVirtualFileSystem vfs, string contentModId,
            string scenarioAssetPath = "Scenario/Scenario.json")
        {
            _vfs = vfs ?? throw new ArgumentNullException(nameof(vfs));
            _contentModId = contentModId ?? throw new ArgumentNullException(nameof(contentModId));
            _scenarioAssetPath = scenarioAssetPath ?? throw new ArgumentNullException(nameof(scenarioAssetPath));
        }

        public string DomainKey => "sango.sim";

        public JsonNode CaptureState()
        {
            Scenario scenario = Scenario.Cur
                ?? throw new InvalidOperationException(
                    "Sango kernel is not booted; sango.sim capture requires Scenario.Cur (SangoKernelBoot.Boot).");

            // 原版 Save 首循环(prepareList 顺序):活引用同步回 Id 字段。
            scenario.forceSet.ForEach(o => o.OnScenarioSave(scenario));
            scenario.corpsSet.ForEach(o => o.OnScenarioSave(scenario));
            scenario.citySet.ForEach(o => o.OnScenarioSave(scenario));
            scenario.personSet.ForEach(o => o.OnScenarioSave(scenario));
            scenario.buildingSet.ForEach(o => o.OnScenarioSave(scenario));
            scenario.troopsSet.ForEach(o => o.OnScenarioSave(scenario));
            scenario.fireSet.ForEach(o => o.OnScenarioSave(scenario));

            InfoCaptureAdjustments(scenario);

            ScenarioCommonData savedCommonData = scenario.CommonData;
            scenario.CommonData = null;
            string json;
            try
            {
                json = JsonConvert.SerializeObject(scenario, SaveSerializerSettings);
            }
            finally
            {
                scenario.CommonData = savedCommonData;
            }

            int[] randomState = GameRandom.ExportState();
            var randomNode = new JsonArray();
            foreach (int value in randomState)
            {
                randomNode.Add(value);
            }

            return new JsonObject
            {
                ["scenario"] = JsonNode.Parse(json),
                ["random"] = randomNode,
                // M3.a:不入档的城内有序名单按活世界序补捕获(见 SangoCityPersonOrder 文件头)。
                // D-1' 退役候选:城域有序名单/经济暂态/job 位已由原生城组件
                // (SangoCityRoster/SangoCityEconomy/SangoCityJobState)随引擎 world.bin
                // 持久化;等价证明(对拍逐位相等)后本捕获节随内核城域终局一并退役。
                ["cityOrder"] = CaptureCityOrder(scenario),
            };
        }

        static JsonObject CaptureCityOrder(Scenario scenario)
        {
            var all = new JsonObject();
            var wild = new JsonObject();
            var invisible = new JsonObject();
            var buildings = new JsonObject();
            scenario.citySet.ForEach(city =>
            {
                if (city == null)
                {
                    return;
                }

                all[city.Id.ToString()] = IdsOf(city.allPersons);
                wild[city.Id.ToString()] = IdsOf(city.wildPersons);
                invisible[city.Id.ToString()] = IdsOf(city.invisiblePersons);
                buildings[city.Id.ToString()] = BuildingIdsOf(city.allBuildings);
            });
            var totalGainFood = new JsonObject();
            var totalGainGold = new JsonObject();
            var populationIncreaseFactor = new JsonObject();
            var extraGainFoodFactor = new JsonObject();
            var extraGainGoldFactor = new JsonObject();
            var extraPopulationFactor = new JsonObject();
            scenario.citySet.ForEach(city =>
            {
                if (city == null)
                {
                    return;
                }

                totalGainFood[city.Id.ToString()] = city.totalGainFood;
                totalGainGold[city.Id.ToString()] = city.totalGainGold;
                populationIncreaseFactor[city.Id.ToString()] = city.population_increase_factor;
                extraGainFoodFactor[city.Id.ToString()] = city.extraGainFoodFactor;
                extraGainGoldFactor[city.Id.ToString()] = city.extraGainGoldFactor;
                extraPopulationFactor[city.Id.ToString()] = city.extraPopulationFactor;
            });
            var troopSkillCd = new JsonObject();
            scenario.troopsSet.ForEach(troop =>
            {
                if (troop == null || !troop.IsAlive)
                {
                    return;
                }

                var skills = new JsonObject();
                foreach (SkillInstance? skill in troop.landSkills)
                {
                    if (skill != null) skills[skill.Name ?? string.Empty] = skill.CDCount;
                }
                foreach (SkillInstance? skill in troop.waterSkills)
                {
                    if (skill != null) skills[skill.Name ?? string.Empty] = skill.CDCount;
                }
                foreach (SkillInstance? skill in troop.StrategySkills)
                {
                    if (skill != null) skills[skill.Name ?? string.Empty] = skill.CDCount;
                }
                troopSkillCd[troop.Id.ToString()] = skills;
            });
            var leaderPerson = new JsonObject();
            var leaderElectionPending = new JsonObject();
            var cityTroopMissionType = new JsonObject();
            var cityTroopMissionTarget = new JsonObject();
            var cityCurActiveTroop = new JsonObject();
            scenario.citySet.ForEach(city =>
            {
                if (city == null)
                {
                    return;
                }

                leaderPerson[city.Id.ToString()] = city.Leader?.Id ?? 0;
                leaderElectionPending[city.Id.ToString()] = LeaderElectionPendingOf(city);
                cityTroopMissionType[city.Id.ToString()] = (int)city.TroopMissionType;
                cityTroopMissionTarget[city.Id.ToString()] = city.TroopMissionTargetId;
                cityCurActiveTroop[city.Id.ToString()] = city.CurActiveTroop?.Id ?? 0;
            });
            var personStates = new JsonObject();
            scenario.personSet.ForEach(person =>
            {
                if (person != null)
                {
                    personStates[person.Id.ToString()] = person.state;
                }
            });
            var forceIsAlive = new JsonObject();
            var forceFightPower = new JsonObject();
            scenario.forceSet.ForEach(force =>
            {
                if (force != null)
                {
                    forceIsAlive[force.Id.ToString()] = force.IsAlive;
                    forceFightPower[force.Id.ToString()] = force.FightPower;
                }
            });
            // 势力同盟名单(Force.AllianceList 不入档,见 SangoCityPersonOrder 第 5 条):
            // SangoObjectList 存储序捕获(ForEach 是倒序遍历语义)。
            var forceAllianceList = new JsonObject();
            scenario.forceSet.ForEach(force =>
            {
                if (force == null)
                {
                    return;
                }

                var allianceIds = new JsonArray();
                foreach (Alliance? alliance in force.AllianceList.objects)
                {
                    if (alliance != null)
                    {
                        allianceIds.Add(alliance.Id);
                    }
                }

                forceAllianceList[force.Id.ToString()] = allianceIds;
            });
            // 俘虏三面(M3.g):Troop/City.captiveList 与 Force.BeCaptiveList 的序列化特性
            // 被上游注释(Troop.cs:94/City.cs:360),按 M1.d"修原版存档 bug"先例在保存面
            // 补捕获,SangoCityPersonOrder 文件头第 6-8 条。
            var troopCaptives = new JsonObject();
            scenario.troopsSet.ForEach(troop =>
            {
                if (troop == null || !troop.IsAlive || troop.captiveList.Count == 0)
                {
                    return;
                }

                troopCaptives[troop.Id.ToString()] = IdsOf(troop.captiveList);
            });
            var cityCaptives = new JsonObject();
            scenario.citySet.ForEach(city =>
            {
                if (city == null || city.captiveList.Count == 0)
                {
                    return;
                }

                cityCaptives[city.Id.ToString()] = IdsOf(city.captiveList);
            });
            var forceBeCaptives = new JsonObject();
            scenario.forceSet.ForEach(force =>
            {
                if (force == null || force.BeCaptiveList.Count == 0)
                {
                    return;
                }

                forceBeCaptives[force.Id.ToString()] = IdsOf(force.BeCaptiveList);
            });
            // 回合内决策面(M3.g):AI 进度位 + 命令队列残余数,与挂起内政演出事件
            // (搜索/登庸,Resolve 在 RenderEvent 队列、消耗随机)——见
            // SangoCityPersonOrder 文件头第 7/8 条。
            var forceAi = new JsonObject();
            scenario.forceSet.ForEach(force =>
            {
                if (force != null)
                {
                    forceAi[force.Id.ToString()] = AiProgressRow(force.AIPrepared, force.AIFinished, force.AICommandList.Count);
                }
            });
            var corpsAi = new JsonObject();
            scenario.corpsSet.ForEach(corps =>
            {
                if (corps != null)
                {
                    corpsAi[corps.Id.ToString()] = AiProgressRow(corps.AIPrepared, corps.AIFinished, corps.AICommandQueue.Count);
                }
            });
            var cityAi = new JsonObject();
            scenario.citySet.ForEach(city =>
            {
                if (city != null)
                {
                    cityAi[city.Id.ToString()] = AiProgressRow(city.AIPrepared, city.AIFinished, city.AICommandList.Count);
                }
            });
            var troopAi = new JsonObject();
            scenario.troopsSet.ForEach(troop =>
            {
                if (troop != null)
                {
                    troopAi[troop.Id.ToString()] = AiProgressRow(troop.AIPrepared, troop.AIFinished, 0);
                }
            });
            return new JsonObject
            {
                ["troopSkillCd"] = troopSkillCd,
                ["allPersons"] = all,
                ["wildPersons"] = wild,
                ["invisiblePersons"] = invisible,
                ["allBuildings"] = buildings,
                ["totalGainFood"] = totalGainFood,
                ["totalGainGold"] = totalGainGold,
                ["populationIncreaseFactor"] = populationIncreaseFactor,
                ["extraGainFoodFactor"] = extraGainFoodFactor,
                ["extraGainGoldFactor"] = extraGainGoldFactor,
                ["leaderPerson"] = leaderPerson,
                ["leaderElectionPending"] = leaderElectionPending,
                ["personStates"] = personStates,
                ["cityTroopMissionType"] = cityTroopMissionType,
                ["cityTroopMissionTarget"] = cityTroopMissionTarget,
                ["cityCurActiveTroop"] = cityCurActiveTroop,
                ["forceIsAlive"] = forceIsAlive,
                ["forceAllianceList"] = forceAllianceList,
                ["forceFightPower"] = forceFightPower,
                ["troopCaptives"] = troopCaptives,
                ["cityCaptives"] = cityCaptives,
                ["forceBeCaptives"] = forceBeCaptives,
                ["aiProgress"] = new JsonObject
                {
                    // D-1' 退役候选:city 段(AIPrepared/AIFinished/命令残余)已入原生
                    // SangoCityJobState/SangoCityAIPlan 组件随 world.bin 持久化。
                    ["force"] = forceAi,
                    ["corps"] = corpsAi,
                    ["city"] = cityAi,
                    ["troop"] = troopAi,
                },
                // D-1' 退役候选:内政演出排程(搜索/登庸)的原生等价物是城 job 组件 +
                // 命令 journal;随演出面迁移波退役。
                ["pendingJobs"] = CapturePendingJobEvents(),
            };
        }

        // needUpdateLeader 是 City 的私有挂起位(重选排在城回合末),捕获面读原值、
        // Apply 侧经公开 NeedUpdateLeader() 补真——不放宽内核封装。
        static readonly System.Reflection.FieldInfo LeaderPendingField = typeof(City)
            .GetField("needUpdateLeader", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?? throw new InvalidOperationException("City.needUpdateLeader backing field is missing; the capture face cannot read the pending election flag.");

        static bool LeaderElectionPendingOf(City city) => (bool)LeaderPendingField.GetValue(city)!;

        // SangoObjectList.ForEach 倒序遍历(内核遍历语义);这里要的是存储正序,直接走
        // objects 下层数组,Apply 侧按同序重排回存储序。
        static JsonArray IdsOf(SangoObjectList<Person> persons)
        {
            var array = new JsonArray();
            foreach (Person? person in persons.objects)
            {
                if (person != null)
                {
                    array.Add(person.Id);
                }
            }
            return array;
        }

        static JsonArray IdsOf(List<Person> persons)
        {
            var array = new JsonArray();
            foreach (Person? person in persons)
            {
                if (person != null)
                {
                    array.Add(person.Id);
                }
            }
            return array;
        }

        static JsonArray BuildingIdsOf(SangoObjectList<Building> list)
        {
            var array = new JsonArray();
            foreach (Building? building in list.objects)
            {
                if (building != null)
                {
                    array.Add(building.Id);
                }
            }
            return array;
        }

        // [prepared, finished, 队列残余命令数]——残余数 + 内核 AIPrepare 的确定性重建
        // 即"已入列命令的语义重放"(Apply 侧削前缀)。
        static JsonArray AiProgressRow(bool prepared, bool finished, int remainingCommands)
        {
            return new JsonArray { prepared, finished, remainingCommands };
        }

        // 挂起的玩法型演出事件(Enter 才结算世界/消耗随机;IsInited=已入 Enter 的不再捕获,
        // 否则回放会二次结算)。只认搜索/同域登庸两类;纯演出(战斗动画等)不入档——
        // M2.c/M3.f 中途存档回归实证其掉落为 digest 中立。
        static JsonArray CapturePendingJobEvents()
        {
            var jobs = new JsonArray();
            var renderEvent = Sango.Render.RenderEvent.Instance;
            foreach (Sango.Render.IRenderEventBase? pendingEvent in
                     EnumerateRenderQueue(renderEvent, "dependsEventQueue").Concat(EnumerateRenderQueue(renderEvent, "eventQueue")))
            {
                if (pendingEvent == null || pendingEvent.IsInited)
                {
                    continue;
                }

                switch (pendingEvent)
                {
                    case Sango.Render.CityPersonSearchingEvent search when search.city != null && search.person != null:
                        jobs.Add(new JsonObject
                        {
                            ["kind"] = "search",
                            ["cityId"] = search.city.Id,
                            ["personId"] = search.person.Id,
                        });
                        break;
                    case Sango.Render.CityRecruitPersonEvent recruit when recruit.person != null && recruit.target != null:
                        jobs.Add(new JsonObject
                        {
                            ["kind"] = "recruit",
                            ["personId"] = recruit.person.Id,
                            ["targetPersonId"] = recruit.target.Id,
                        });
                        break;
                }
            }

            return jobs;
        }

        static IEnumerable<Sango.Render.IRenderEventBase> EnumerateRenderQueue(Sango.Render.RenderEvent renderEvent, string fieldName)
        {
            return (List<Sango.Render.IRenderEventBase>)typeof(Sango.Render.RenderEvent)
                .GetField(fieldName, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .GetValue(renderEvent)!;
        }

        public void RestoreState(JsonNode state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (state is not JsonObject root)
                throw new SaveContextException("Save domain 'sango.sim' must be an object.");

            JsonNode scenarioNode = root["scenario"]
                ?? throw new SaveContextException("Save domain 'sango.sim' is missing 'scenario'.");
            if (root["random"] is not JsonArray randomArray)
                throw new SaveContextException("Save domain 'sango.sim' is missing 'random'.");

            var randomState = new int[randomArray.Count];
            for (int i = 0; i < randomArray.Count; i++)
            {
                if (randomArray[i] is not JsonValue value || !value.TryGetValue<int>(out randomState[i]))
                {
                    throw new SaveContextException($"Save domain 'sango.sim' random[{i}] must be an integer.");
                }
            }

            SangoKernelBoot.Restore(_vfs, _contentModId, scenarioNode.ToJsonString(), randomState,
                _scenarioAssetPath, SangoCityPersonOrder.FromJson(root["cityOrder"]));
        }

        // 原版 Save 对 Info 的两处置写 + 当前势力保真:
        // isSave 置真让回灌侧跳过 Variables/Map 重建、改由 JSON 填充;dateTime 是存档时间戳
        // 元数据(UI 显示用,不参与 digest)。curForceId:M1.d 曾一律清零(边界存档的忠实
        // 表示,队列由 MakeForceQuene 全量重建);M3.a 玩家局的静止态是"回合中"(玩家势力
        // 阻塞在君主军团),清零会让回灌从队列头重放该势力的回合开始(重扣军粮/重发行动力),
        // 与直跑链分叉——改为存档时保留 CurRunForce(回合边界存档时为 null,语义与清零同)。
        // Start() 的恢复分支按 curForceId 静默 drain 到当前势力,不重跑 OnForceTurnStart,
        // 即原版"玩家回合中存档"的语义。
        static void InfoCaptureAdjustments(Scenario scenario)
        {
            scenario.Info.isSave = true;
            scenario.Info.dateTime = DateTime.Now.ToFileTime();
            scenario.Info.curForceId = scenario.CurRunForce?.Id ?? 0;
        }

        // 与原版 Scenario.Save 同一设置(Formatting 仅影响体积,内存面取紧凑)。
        static readonly JsonSerializerSettings SaveSerializerSettings = new()
        {
            NullValueHandling = NullValueHandling.Ignore,
            ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
        };
    }
}
