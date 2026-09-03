// M2.b 部队编成/移动 + M2.c 战斗分派内核操作:原版 UI→内核链的 headless 等价展开,
// 命令层(SangoWebUiMod sango.createTroop/sango.moveTroop)、测试播种与演示播种共用,
// 不再有平行实现。
//   编成:UICityExpedition.OnOK → CityExpedition.DoJob(Game/System/City/CityExpedition.cs)
//     门槛 IsValid:城兵力>0 && 城军粮>0 && 空闲武将>0 && 军团行动力>=MakeTroop 费;
//     表单初值 OnEnter/UpdateJobValue:可选兵种=TroopType.CheckActivTroopTypeList(城 freePersons),
//     兵力 clamp = min(请求, MaxTroops, 城兵力, 兵装 CheckCostMin×2),携粮/携金 ≤ 城库存;
//     结算:兵装 Cost×2 → 城兵/粮/金扣减 → 武将出列 → 军团扣行动力 → City.EnsureTroop
//     (落位城中心格 + scenario.Add + Troop.Init 属性/技能全量计算)。
//   移动:TroopSystem.OnEnter(重建 MoveRange)→ HandleEvent Click(移动范围内落格)→
//     TroopActionStay.OnEnter(逐步 TroopMoveEvent,原版由渲染队列逐帧结算)→
//     OnMoveDone(ActionOver=true)。headless 下部队 Render 不可见,事件一拍即结,
//     这里显式泵空渲染队列后返回,路径步数与 Map.GetMovePath 一致。
//   战斗分派(M2.c):TroopSystem.HandleEvent Click → TroopInteractiveDialog(在程占位)
//     与 TroopInteractiveMenu(范围外右键委任菜单)的合流展开(Game/System/Troop/
//     Interactive/*,订阅者 Check 条件为分派依据,UI 状态机不搬,见 SangoUnityShim 裁定):
//     敌部队 = TroopInteractiveDestroyTroop:歼灭任务(TroopDestroyTroop 行为,
//       TroopAIUtility.PriorityAction 选移位+技能,SpellSkill 一击解算,反击/击退在
//       SkillInstance.Action 内)。范围无关——敌占格永远进不了 MoveRange
//       (Cell.CanPassThrough 势力门),原版对敌目标的攻击入口本就是委任菜单;
//     敌城 = TroopInteractiveOccupyCity:占城任务(TroopOccupyCity 行为:逼近→攻城→
//       城陷,City.ChangeTroops/ChangeDurability/OnFall→OnCityFall/OnForceFall 链);
//     同势力城(在程)= TroopInteractiveCityEnter:走移动路径 → ActionOver → EnterCity;
//     己方未完工/破损建筑 = TroopInteractiveBuildingFix(TroopFixBuilding 任务,
//       原 Check 无范围条件)。
//     任务型交互的逐帧 Update(TroopInteractive*.Update 每帧一次 DoAI)压缩为
//     "DoAI 一拍 + 泵空演出队列" 循环,直至 DoAI 完成(ActionOver)。
// 范围外目标在原版走"委任移动"确认链(TroopInteractiveMoveToCell,多回合任务制),
// M2.b 不复用该链,按越程拒绝。
// 上游怪癖(保留):TroopInteractiveBase.Start 对玩家控部队 ClearMission 后再授新任务,
// AI 托管部队旧任务直接覆盖(SetMission);运输队(IsTransport)不得歼灭/占城。

using System;
using System.Collections.Generic;
using Sango.Core;
using Sango.Render;

namespace Sango.Runtime
{
    /// <summary>编成/移动/战斗操作的类型化结果(命令层原样转 WebUiCommandResult)。</summary>
    public sealed record SangoTroopOpResult(bool Succeeded, string ErrorCode, string Message)
    {
        public static SangoTroopOpResult Ok(string action = "") => new(true, string.Empty, action);

        public static SangoTroopOpResult Fail(string errorCode, string message) =>
            new(false, errorCode, message);
    }

    public static class SangoTroopOps
    {
        /// <summary>原版出征窗口的武将上限(PersonSelectSystem.Start 的 cap=3)。</summary>
        public const int MaxMembers = 3;

        /// <summary>渲染事件泵的步进上限:一拍一事件,超界即视为事件链卡死,fail-fast。</summary>
        private const int MaxRenderEventPumpCalls = 10_000;

        /// <summary>
        /// 出征编成(UICityExpedition 表单终态 → DoJob)。personIds 必须全落在城 freePersons,
        /// land/waterTroopTypeId 必须在该城可组兵种表内(缺省取两类第 0 项,同 OnEnter 默认);
        /// troops/gold/food 是表单滑条终值,按原版滑条语义 clamp 后生效。
        /// </summary>
        public static (SangoTroopOpResult Result, Troop? Troop) CreateTroop(
            Scenario scenario,
            City city,
            IReadOnlyList<int> personIds,
            int? landTroopTypeId = null,
            int? waterTroopTypeId = null,
            int troops = 0,
            int gold = 0,
            int food = 0)
        {
            if (scenario == null) throw new ArgumentNullException(nameof(scenario));
            if (city == null) throw new ArgumentNullException(nameof(city));
            if (personIds == null) throw new ArgumentNullException(nameof(personIds));

            if (city.mBelongForce == null || city.mBelongCorps == null)
            {
                return (SangoTroopOpResult.Fail("city_not_owned", "createTroop requires an owned city (force + corps)."), null);
            }

            // M3.a 玩家门(原版出征窗口从城菜单进入,同 CityBaseSystem 菜单门)。
            SangoTroopOpResult? gate = SangoPlayerTurnOps.CityCommandGate(scenario, city);
            if (gate != null)
            {
                return (gate, null);
            }

            // CityExpedition.IsValid。
            int jobCostAP = JobType.GetJobCostAP((int)CityJobType.MakeTroop);
            if (city.troops <= 0 || city.food <= 0 || city.freePersons.Count == 0 ||
                city.mBelongCorps.ActionPoint < jobCostAP)
            {
                return (SangoTroopOpResult.Fail("invalid_state",
                    "CityExpedition.IsValid gate rejected the order (city troops / food / free persons / corps action points)."), null);
            }

            if (personIds.Count == 0 || personIds.Count > MaxMembers)
            {
                return (SangoTroopOpResult.Fail("invalid_payload",
                    $"createTroop requires 1..{MaxMembers} personIds (PersonSelectSystem cap)."), null);
            }

            var persons = new List<Person>(personIds.Count);
            foreach (int personId in personIds)
            {
                Person? person = city.freePersons.FirstOrDefault(candidate => candidate != null && candidate.Id == personId);
                if (person == null)
                {
                    return (SangoTroopOpResult.Fail("person_not_free",
                        $"person {personId} is not in the city freePersons list."), null);
                }

                persons.Add(person);
            }

            // OnEnter:可组兵种表来自全城空闲武将(不只选中者),同原版。
            var activeTroopTypes = new List<TroopType>();
            TroopType.CheckActivTroopTypeList(city.freePersons, activeTroopTypes);
            List<TroopType> landTypes = activeTroopTypes.FindAll(type => type.isLand);
            List<TroopType> waterTypes = activeTroopTypes.FindAll(type => !type.isLand);
            if (landTypes.Count == 0 || waterTypes.Count == 0)
            {
                return (SangoTroopOpResult.Fail("no_active_troop_type",
                    "TroopType.CheckActivTroopTypeList produced no land or no water type for this city roster."), null);
            }

            TroopType landType = SelectTroopType(landTypes, landTroopTypeId);
            if (landType == null)
                return (SangoTroopOpResult.Fail("invalid_troop_type", "landTroopTypeId is outside the active land type list."), null);
            TroopType waterType = SelectTroopType(waterTypes, waterTroopTypeId);
            if (waterType == null)
                return (SangoTroopOpResult.Fail("invalid_troop_type", "waterTroopTypeId is outside the active water type list."), null);

            // 表单体(TargetTroop 终态):UpdateJobValue 的成员/兵种/MaxTroops 链。
            var troop = new Troop
            {
                Leader = persons[0],
                Member1 = persons.Count > 1 ? persons[1] : null,
                Member2 = persons.Count > 2 ? persons[2] : null,
                LandTroopType = landType,
                WaterTroopType = waterType,
                morale = city.morale,
                energy = city.energy,
            };
            troop.CalculateMaxTroops();

            // 滑条 clamp(OnTroopsSliderValueChanged/SetTroops 语义):
            // 兵力 = min(请求, MaxTroops, 城兵力, 兵装两表 CheckCostMin);粮/金 ≤ 城库存。
            int effectiveTroops = Math.Clamp(troops, 1, troop.MaxTroops);
            effectiveTroops = Math.Min(effectiveTroops, city.troops);
            effectiveTroops = city.itemStore.CheckCostMin(landType.costItems, effectiveTroops);
            effectiveTroops = city.itemStore.CheckCostMin(waterType.costItems, effectiveTroops);
            troop.troops = effectiveTroops;
            troop.food = Math.Min(Math.Max(food, 0), city.food);
            troop.gold = Math.Min(Math.Max(gold, 0), city.gold);
            troop.CalculateAttribute(scenario);

            // DoJob 前置(静默 early-return 在命令层转为类型化失败)。
            if (troop.troops <= 0)
                return (SangoTroopOpResult.Fail("invalid_troops", "DoJob gate: effective troop size is zero after clamps."), null);
            if (troop.food <= 0)
                return (SangoTroopOpResult.Fail("invalid_food", "DoJob gate: troop food is zero (city food exhausted or request 0)."), null);

            // DoJob 结算序。
            troop.ActionOver = false;
            troop.IsAlive = true;
            landType.Cost(city, troop.troops);
            waterType.Cost(city, troop.troops);
            city.troops -= troop.troops;
            city.food -= troop.food;
            city.gold -= troop.gold;
            troop.ForEachPerson(person => city.freePersons.Remove(person));
            city.mBelongCorps.ReduceActionPoint(jobCostAP);
            city.EnsureTroop(troop, scenario);
            SangoCommandJournal.Record(
                SangoReplayJournal.CreateTroopKind,
                new SangoCreateTroopArgs(city.Id, ToArray(personIds), landTroopTypeId, waterTroopTypeId, troops, gold, food, troop.Id));
            return (SangoTroopOpResult.Ok(), troop);
        }

        static int[] ToArray(IReadOnlyList<int> personIds)
        {
            var ids = new int[personIds.Count];
            for (int i = 0; i < personIds.Count; i++)
            {
                ids[i] = personIds[i];
            }

            return ids;
        }

        /// <summary>
        /// 移动(移动范围内单步落地,含原地待命)。返回 (结果, 路径步数含起点)。
        /// 路径结算与 TroopActionStay 同源:Map.GetMovePath → 逐步 TroopMoveEvent → 泵空 → ActionOver。
        /// 成功路径入命令 journal(result.Message 即行动型:待命/enter-city/field-strike/occupation/
        /// building-fix,重放侧按 troopId+x+y 重放同一分派)。
        /// </summary>
        public static (SangoTroopOpResult Result, int PathSteps) MoveTroop(Scenario scenario, Troop troop, Cell destCell)
        {
            (SangoTroopOpResult result, int pathSteps) = MoveTroopCore(scenario, troop, destCell);
            if (result.Succeeded)
            {
                SangoCommandJournal.Record(
                    SangoReplayJournal.MoveTroopKind,
                    new SangoMoveTroopArgs(troop.Id, destCell!.x, destCell.y, result.Message));
            }

            return (result, pathSteps);
        }

        static (SangoTroopOpResult Result, int PathSteps) MoveTroopCore(Scenario scenario, Troop troop, Cell destCell)
        {
            if (scenario == null) throw new ArgumentNullException(nameof(scenario));
            if (troop == null) throw new ArgumentNullException(nameof(troop));

            if (!troop.IsAlive)
                return (SangoTroopOpResult.Fail("troop_dead", "target troop is not alive."), 0);
            if (troop.ActionOver)
                return (SangoTroopOpResult.Fail("troop_acted", "target troop has already acted this turn."), 0);
            if (destCell == null)
                return (SangoTroopOpResult.Fail("invalid_cell", "target cell is outside the map."), 0);

            // M3.a 玩家门(原版 TroopActionBase 菜单门):无玩家恒过,玩家局内只放行
            // 玩家势力且当前行动的部队。
            SangoTroopOpResult? gate = SangoPlayerTurnOps.TroopCommandGate(scenario, troop);
            if (gate != null)
                return (gate, 0);

            // TroopSystem.OnEnter:进入部队命令态先重建移动范围(ZOC/水陆移动力全套)。
            troop.MoveRange.Clear();
            scenario.Map.GetMoveRange(troop, troop.MoveRange);

            // 委任分派先行:敌对目标(敌部队/敌城)与己方破损建筑不受移动范围限制
            // (原版委任菜单语义;敌占格本就进不了 MoveRange,见文件头)。
            if (IsDelegateInteraction(troop, destCell))
                return InteractAtCell(scenario, troop, destCell);

            if (!troop.MoveRange.Contains(destCell))
                return (SangoTroopOpResult.Fail("out_of_range",
                    "target cell is outside the troop move range (original UI routes it to the multi-turn delegate-move chain, not available here)."), 0);

            // HandleEvent Click 分派:空格/本格走待命;同势力城在程走入城;其余占位
            // (完好己方建筑/同阵营部队)在原版对话框无订阅者匹配,保持占位拒绝。
            if (!(destCell == troop.cell && destCell.building == null) && !destCell.IsEmpty())
            {
                if (destCell.building != null && destCell.building.IsCityBase() &&
                    destCell.building.mBelongForce == troop.mBelongForce)
                {
                    return EnterSameForceCity(scenario, troop, destCell);
                }

                return (SangoTroopOpResult.Fail("occupied_target",
                    "target cell holds a friendly troop or an intact friendly building; the original dialog offers no interaction there."), 0);
            }

            var movePath = new List<Cell>(troop.MoveRange.Count);
            scenario.Map.GetMovePath(troop, destCell, movePath);
            var onMoveDone = (System.Action)(() => troop.ActionOver = true);

            // TroopActionStay.OnEnter:≤1 步即原地待命直接完成;否则逐步建 TroopMoveEvent,
            // 末步回调即 OnMoveDone(待命完成,行动结束)。
            if (movePath.Count > 1)
            {
                Cell start = troop.cell;
                for (int i = 1; i < movePath.Count; i++)
                {
                    bool isLast = i == movePath.Count - 1;
                    Cell dest = movePath[i];
                    TroopMoveEvent moveEvent = RenderEvent.Instance.Create<TroopMoveEvent>();
                    moveEvent.Init(troop, start, dest, isLast, isLast ? onMoveDone : null);
                    RenderEvent.Instance.Add(moveEvent);
                    start = dest;
                }

                PumpRenderEvents(scenario);
            }
            else
            {
                onMoveDone();
            }

            return (SangoTroopOpResult.Ok(), movePath.Count);
        }

        // 任务型交互的单回合步进预算:一次接敌任务每拍至多"一次 DoAI + 排空演出队列"
        // (移动 N 格 + 一次技能解算),量级远小于此;触顶即视为任务链卡死,fail-fast。
        private const int MaxMissionPumpRounds = 10_000;

        /// <summary>
        /// 委任交互判定:敌部队占格、非同势力城基、己方破损/在建建筑。三者对应原版
        /// TroopInteractiveDestroyTroop / OccupyCity / BuildingFix 的 Check(范围条件见各分支),
        /// 是 moveTroop 的战斗入口;其余占位(同阵营部队、完好己方建筑)不在此列。
        /// </summary>
        static bool IsDelegateInteraction(Troop troop, Cell destCell)
        {
            if (destCell.troop != null && troop.IsEnemy(destCell.troop))
            {
                return true;
            }

            if (destCell.building != null)
            {
                if (destCell.building.IsCityBase())
                {
                    return destCell.building.mBelongForce != troop.mBelongForce;
                }

                return destCell.building.mBelongForce == troop.mBelongForce &&
                       (destCell.building.isUpgrading || destCell.building.durability < destCell.building.DurabilityLimit);
            }

            return false;
        }

        /// <summary>
        /// TroopInteractive*(歼灭/占城/修补)分派:IsDelegateInteraction 命中后按占位物类型
        /// 授任务并当回合压缩执行。城基优先(敌城在部队+城同格时仍以城为攻击对象,
        /// 与原版 TroopInteractiveOccupyCity 的任务目标一致),敌部队次之,己方建筑末位。
        /// </summary>
        static (SangoTroopOpResult Result, int PathSteps) InteractAtCell(Scenario scenario, Troop troop, Cell destCell)
        {
            if (destCell.building != null && destCell.building.IsCityBase())
            {
                // TroopInteractiveOccupyCity.Check:非同势力城基(敌/白城),运输队拒。
                if (troop.IsTransport)
                {
                    return (SangoTroopOpResult.Fail("troop_transport",
                        "transport troops cannot occupy cities (TroopInteractiveOccupyCity gate)."), 0);
                }

                return RunTroopMission(scenario, troop, MissionType.TroopOccupyCity,
                    ((City)destCell.building).Id, "occupation");
            }

            // TroopInteractiveDestroyTroop.Check:敌部队占格(运输队拒)。
            if (destCell.troop != null && troop.IsEnemy(destCell.troop))
            {
                if (troop.IsTransport)
                {
                    return (SangoTroopOpResult.Fail("troop_transport",
                        "transport troops cannot engage in field combat (TroopInteractiveDestroyTroop gate)."), 0);
                }

                return RunTroopMission(scenario, troop, MissionType.TroopDestroyTroop,
                    destCell.troop.Id, "field-strike");
            }

            // TroopInteractiveBuildingFix.Check:己方非城基建筑,升级中或耐久破损(无范围条件)。
            return RunTroopMission(scenario, troop, MissionType.TroopFixBuilding,
                destCell.building!.Id, "building-fix");
        }

        /// <summary>
        /// TroopInteractiveCityEnter 的移动+入城:逐步 TroopMoveEvent 走完路径(与移动链同源),
        /// OnMoveDone 置 ActionOver,落格仍是同势力城基才 EnterCity(原版终判)。
        /// </summary>
        static (SangoTroopOpResult Result, int PathSteps) EnterSameForceCity(Scenario scenario, Troop troop, Cell destCell)
        {
            var movePath = new List<Cell>(troop.MoveRange.Count);
            scenario.Map.GetMovePath(troop, destCell, movePath);

            var onMoveDone = (System.Action)(() =>
            {
                troop.ActionOver = true;
                if (destCell.building != null && destCell.building.IsSameForce(troop) && destCell.building.IsCityBase())
                {
                    troop.EnterCity((City)destCell.building);
                }
            });

            if (movePath.Count > 1)
            {
                Cell start = troop.cell;
                for (int i = 1; i < movePath.Count; i++)
                {
                    bool isLast = i == movePath.Count - 1;
                    Cell dest = movePath[i];
                    TroopMoveEvent moveEvent = RenderEvent.Instance.Create<TroopMoveEvent>();
                    moveEvent.Init(troop, start, dest, isLast, isLast ? onMoveDone : null);
                    RenderEvent.Instance.Add(moveEvent);
                    start = dest;
                }

                PumpRenderEvents(scenario);
            }
            else
            {
                onMoveDone();
            }

            return (SangoTroopOpResult.Ok("enter-city"), movePath.Count);
        }

        /// <summary>
        /// TroopInteractive*(歼灭/占城/修建)OnEnter→Update 链的当回合压缩:
        /// SetMission(玩家控先 ClearMission,原 Base.Start 语义)后逐拍"DoAI 一次 + 泵空演出
        /// 队列",直至 DoAI 完成或部队溃灭/已行动;任务完成后 ActionOver 由 DoAI 自置
        /// (OnAIDone 同义),这里只兜底(溃灭路径 DoAI 可能不再被调)。
        /// </summary>
        static (SangoTroopOpResult Result, int PathSteps) RunTroopMission(
            Scenario scenario, Troop troop, MissionType missionType, int missionTarget, string action)
        {
            if (troop.IsPlayerControl)
            {
                troop.ClearMission();
            }

            troop.SetMission(missionType, missionTarget);

            bool completed = false;
            for (int i = 0; i < MaxMissionPumpRounds; i++)
            {
                if (!troop.IsAlive || troop.ActionOver)
                {
                    completed = true;
                    break;
                }

                if (troop.DoAI(scenario))
                {
                    completed = true;
                    break;
                }

                PumpRenderEvents(scenario);
            }

            if (!completed)
            {
                throw new InvalidOperationException(
                    $"troop {troop.Id} mission {missionType} stalled after {MaxMissionPumpRounds} DoAI/pump rounds; the mission chain is waiting on player input or never completes.");
            }

            PumpRenderEvents(scenario);
            if (troop.IsAlive)
            {
                troop.ActionOver = true;
            }

            return (SangoTroopOpResult.Ok(action), 0);
        }

        static TroopType? SelectTroopType(List<TroopType> types, int? requestedId)
        {
            if (requestedId == null)
                return types[0];
            return types.Find(type => type.Id == requestedId.Value);
        }

        // headless 部队 Render 不可见(ObjectRender.IsVisible 恒 false),每个 TroopMoveEvent
        // 一拍即结;泵到队列排空,语义与原版"渲染队列驱动 Scenario.Run"一致,只是压缩时间。
        static void PumpRenderEvents(Scenario scenario)
        {
            for (int i = 0; i < MaxRenderEventPumpCalls; i++)
            {
                if (RenderEvent.Instance.Update(scenario, SangoTurnDriver.VirtualFrameSeconds))
                {
                    return;
                }
            }

            throw new InvalidOperationException(
                "TroopMoveEvent chain stalled while pumping the render event queue; a render event is waiting on player input or never completes.");
        }
    }
}
