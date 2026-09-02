// M2.b 部队编成/移动内核操作:原版两条 UI→内核链的 headless 等价展开,命令层
// (SangoWebUiMod sango.createTroop/sango.moveTroop)、测试播种与演示播种共用,
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
// 范围外目标在原版走"委任移动"确认链(TroopInteractiveMoveToCell,多回合任务制),
// M2.b 不复用该链,按越程拒绝;有建筑/部队占位的落格在原版走接近/战斗对话框(M2.c)。

using System;
using System.Collections.Generic;
using Sango.Core;
using Sango.Render;

namespace Sango.Runtime
{
    /// <summary>编成/移动操作的类型化结果(命令层原样转 WebUiCommandResult)。</summary>
    public sealed record SangoTroopOpResult(bool Succeeded, string ErrorCode, string Message)
    {
        public static SangoTroopOpResult Ok() => new(true, string.Empty, string.Empty);

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
            return (SangoTroopOpResult.Ok(), troop);
        }

        /// <summary>
        /// 移动(移动范围内单步落地,含原地待命)。返回 (结果, 路径步数含起点)。
        /// 路径结算与 TroopActionStay 同源:Map.GetMovePath → 逐步 TroopMoveEvent → 泵空 → ActionOver。
        /// </summary>
        public static (SangoTroopOpResult Result, int PathSteps) MoveTroop(Scenario scenario, Troop troop, Cell destCell)
        {
            if (scenario == null) throw new ArgumentNullException(nameof(scenario));
            if (troop == null) throw new ArgumentNullException(nameof(troop));

            if (!troop.IsAlive)
                return (SangoTroopOpResult.Fail("troop_dead", "target troop is not alive."), 0);
            if (troop.ActionOver)
                return (SangoTroopOpResult.Fail("troop_acted", "target troop has already acted this turn."), 0);
            if (destCell == null)
                return (SangoTroopOpResult.Fail("invalid_cell", "target cell is outside the map."), 0);

            // TroopSystem.OnEnter:进入部队命令态先重建移动范围(ZOC/水陆移动力全套)。
            troop.MoveRange.Clear();
            scenario.Map.GetMoveRange(troop, troop.MoveRange);

            if (!troop.MoveRange.Contains(destCell))
                return (SangoTroopOpResult.Fail("out_of_range",
                    "target cell is outside the troop move range (original UI routes it to the multi-turn delegate-move chain, not available here)."), 0);

            // HandleEvent Click 分派:空格/本格走行动菜单;有建筑或部队占位走接近/战斗对话框(M2.c)。
            if (!(destCell == troop.cell && destCell.building == null) && !destCell.IsEmpty())
                return (SangoTroopOpResult.Fail("occupied_target",
                    "target cell holds a building or troop; approach/attack interactions arrive with M2.c combat."), 0);

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
