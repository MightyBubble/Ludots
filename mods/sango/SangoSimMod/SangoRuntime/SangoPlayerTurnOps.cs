// M3.a 玩家回合操作层:原版玩家接入链在 headless/Web 命令面的落点。
// 原版语义逆向(源:sango-src Project/Assets/Sango/Scripts):
//   势力选定 window_scenario_force_select(UIScenarioForceSelect.StartGame)在 StartScenario
//   前直接写 Scenario.CurSelected.Info.playerForceList;StartScenario → CheckPlayer
//   (Game/Scenario/Scenario.cs:700)按该列表置 Force.IsPlayer=true,MakeForceQuene 把
//   玩家势力排到队列最前。Boot 的 playerForceId 参数即这条正式数据面。
//   玩家回合是阻塞式:Force.Run 对玩家势力跳过 DoAI(Game/Object/Force/Force.cs:564),
//   君主亲领军团在 Corps.Run 触发 GameEvent.OnPlayerControl 后返回 false
//   (Game/Object/Corps/Corps.cs:474),RunForces 即停在该势力等待玩家。
//   解阻塞 = UI「进行」按钮推 PlayerEndTurn 系统(Game/System/Game/PlayerEndTurn.cs):
//   确认后触发 GameEvent.OnPlayerEndTurn,再把带任务未行动部队逐个 DoAI,最后置
//   force.CurRunCorps.ActionOver=true 放行。本文件的 EndPlayerTurn 即这段 Update 体的
//   等价展开(对话框/窗口属 UI 层,不在内核操作面)。
// 玩家命令门:原版 CityBaseSystem.OnCityContextMenuShow / TroopActionBase 的菜单门 =
//   目标对象所属势力 IsPlayer 且 == Scenario.Cur.CurRunForce;SangoCityOps/SangoTroopOps
//   在存在玩家势力时执行同一门(全托管世界无玩家,门恒开,保持 M1/M2 语义)。

using System;
using Ludots.Core.Modding;
using Sango.Core;

namespace Sango.Runtime
{
    public static class SangoPlayerTurnOps
    {
        /// <summary>
        /// 开局势力选择(原版 window_scenario_force_select 数据面):以同种子带玩家重装世界。
        /// 仅限开局一次——世界尚未推进(Info.turnCount==0);已推进的局换势力等价于改写
        /// 历史,类型化拒绝。选势力前的零星命令经重装被对称丢弃(实录与重放同序列,见
        /// SangoReplayJournal.Apply)。成功后记 world-setup 型 journal 项。
        /// </summary>
        public static BootResult SelectPlayerForce(IVirtualFileSystem vfs, string contentModId, int seed, int forceId,
            string scenarioAssetPath = "Scenario/Scenario.json")
        {
            ArgumentNullException.ThrowIfNull(vfs);
            ArgumentNullException.ThrowIfNull(contentModId);
            if (forceId <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(forceId), "selectPlayerForce requires a positive force id.");
            }

            Scenario? current = Scenario.Cur;
            if (current != null && current.Info.turnCount != 0)
            {
                throw new InvalidOperationException(
                    $"selectPlayerForce is an opening-time act; the world has already advanced to turn {current.Info.turnCount}.");
            }

            BootResult result = SangoKernelBoot.BootWithPlayer(vfs, contentModId, seed, forceId, scenarioAssetPath);
            Force? force = result.Scenario.forceSet.Get(forceId);
            if (force == null || !force.IsPlayer)
            {
                throw new InvalidOperationException(
                    $"force {forceId} did not resolve to a player force via CheckPlayer; the scenario forceSet has no such id.");
            }

            SangoCommandJournal.Record(SelectPlayerForceKind, new SangoSelectPlayerForceArgs(forceId, seed));
            return result;
        }

        /// <summary>当前玩家势力 id(Info.playerForceList 首项;0 = 全托管,无玩家)。</summary>
        public static int PlayerForceId(Scenario scenario)
        {
            ArgumentNullException.ThrowIfNull(scenario);
            int[]? list = scenario.Info.playerForceList;
            return list is { Length: > 0 } ? list[0] : 0;
        }

        /// <summary>玩家势力是否仍在局中(灭亡后回合队列不再包含它,门随之退化为全托管)。</summary>
        public static bool PlayerForceAlive(Scenario scenario)
        {
            int forceId = PlayerForceId(scenario);
            if (forceId == 0)
            {
                return false;
            }

            Force? force = scenario.forceSet.Get(forceId);
            return force != null && force.IsAlive;
        }

        /// <summary>
        /// 世界是否停在本玩家的回合等待行动:当前行动势力为玩家,且其正在跑的军团未结束
        /// (即 Corps.Run 的 OnPlayerControl 阻塞位,CurRunCorps 停在君主亲领军团)。
        /// </summary>
        public static bool AwaitingPlayer(Scenario scenario)
        {
            ArgumentNullException.ThrowIfNull(scenario);
            Force? current = scenario.CurRunForce;
            if (current == null || !current.IsPlayer)
            {
                return false;
            }

            return current.CurRunCorps != null && !current.CurRunCorps.ActionOver;
        }

        /// <summary>
        /// 「进行」(原版 PlayerEndTurn.Update 体):要求世界正等待玩家;触发
        /// OnPlayerEndTurn 事件,带任务未行动部队逐个 DoAI 至完成,再放行当前军团。
        /// 成功入 journal(kind=endPlayerTurn)。
        /// </summary>
        public static void EndPlayerTurn()
        {
            Scenario scenario = Scenario.Cur
                ?? throw new InvalidOperationException("Sango kernel is not booted; call SangoKernelBoot.Boot first.");
            Force force = scenario.CurRunForce
                ?? throw new InvalidOperationException("No force is currently running; the player turn cannot end outside a force turn.");
            if (!force.IsPlayer || !AwaitingPlayer(scenario))
            {
                throw new InvalidOperationException(
                    $"The world is not waiting on the player (curForce={scenario.Info.curForceId} {scenario.Info.curForceName}); " +
                    "EndPlayerTurn mirrors the 进行 button and is only valid during the player's blocked turn.");
            }

            GameEvent.OnPlayerEndTurn?.Invoke(force, scenario);

            // 原版逐帧重试 DoAI 且渲染队列同帧推进(M3.c:移动任务的 MoveTo 靠 TroopMoveEvent
            // 逐帧结算,只重试 DoAI 不排空演出队列会永远等 isMoving);headless 以有界泵
            // "DoAI 一拍 + 虚拟帧长排空演出" 等效推进,超界即事件链卡死 fail-fast。
            const int maxTroopAiPumpCalls = 10_000;
            for (int i = 0; i < scenario.troopsSet.Count; ++i)
            {
                Troop? troop = scenario.troopsSet[i];
                if (troop != null && troop.IsAlive && troop.mBelongForce == force && !troop.ActionOver && troop.missionType > 0)
                {
                    int pumps = 0;
                    while (!troop.DoAI(scenario))
                    {
                        if (++pumps > maxTroopAiPumpCalls)
                        {
                            throw new InvalidOperationException(
                                $"troop {troop.Id} mission AI stalled after {maxTroopAiPumpCalls} DoAI calls during EndPlayerTurn.");
                        }

                        Sango.Render.RenderEvent.Instance.Update(scenario, SangoTurnDriver.VirtualFrameSeconds);
                    }
                    troop.Render?.UpdateRender();
                }
            }

            if (force.CurRunCorps != null)
            {
                force.CurRunCorps.ActionOver = true;
            }

            SangoCommandJournal.Record(EndPlayerTurnKind, SangoStepArgs.Instance);
        }

        public const string SelectPlayerForceKind = "selectPlayerForce";
        public const string EndPlayerTurnKind = "endPlayerTurn";

        /// <summary>
        /// 城命令玩家门:无玩家(全托管)恒过;有玩家时要求城属玩家势力且玩家势力正在
        /// 行动(原版 CityBaseSystem.OnCityContextMenuShow 的菜单门)。不过门返回类型化
        /// 失败,过门返回 null。
        /// </summary>
        public static SangoTroopOpResult? CityCommandGate(Scenario scenario, City city)
        {
            ArgumentNullException.ThrowIfNull(scenario);
            ArgumentNullException.ThrowIfNull(city);
            if (PlayerForceId(scenario) == 0)
            {
                return null;
            }

            if (city.mBelongForce == null || !city.mBelongForce.IsPlayer)
            {
                return SangoTroopOpResult.Fail("not_player_city",
                    "a player force is in play; city commands only target the player's own cities (city menu gate).");
            }

            if (scenario.CurRunForce == null || city.mBelongForce != scenario.CurRunForce)
            {
                return SangoTroopOpResult.Fail("not_player_turn",
                    "city commands are only accepted during the player force's current turn (city menu gate).");
            }

            return null;
        }

        /// <summary>
        /// 部队命令玩家门(出征编成同用城门):无玩家恒过;有玩家时要求部队属玩家势力且
        /// 玩家势力正在行动(原版 TroopActionBase 的菜单门)。
        /// </summary>
        public static SangoTroopOpResult? TroopCommandGate(Scenario scenario, Troop troop)
        {
            ArgumentNullException.ThrowIfNull(scenario);
            ArgumentNullException.ThrowIfNull(troop);
            if (PlayerForceId(scenario) == 0)
            {
                return null;
            }

            if (troop.mBelongForce == null || !troop.mBelongForce.IsPlayer)
            {
                return SangoTroopOpResult.Fail("not_player_troop",
                    "a player force is in play; troop commands only target the player's own troops (troop action gate).");
            }

            if (scenario.CurRunForce == null || troop.mBelongForce != scenario.CurRunForce)
            {
                return SangoTroopOpResult.Fail("not_player_turn",
                    "troop commands are only accepted during the player force's current turn (troop action gate).");
            }

            return null;
        }
    }

    /// <summary>selectPlayerForce 的 world-setup 参数面(重放侧据此带玩家重装世界)。</summary>
    public sealed record SangoSelectPlayerForceArgs(int ForceId, int Seed);
}
