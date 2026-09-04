// sango 回合驱动(M1.b):把"推进一个 sango 回合"收敛为单一静态入口,测试与
// Ludots TurnAdvanced 适配器共用。
// 原版回合链(Scenario.Run,逐 Unity 帧驱动):
//   RenderEvent 闸 → TurnStart → RunForces(逐势力 Force.Run:AI 路线 DoAI/军团结算)
//   → TurnEnd(turnCount++) → IncreaseDate(旬/月/年结算) → MakeForceQuene。
// headless 下以虚拟帧长驱动同一 Run() 循环,直至 turnCount 递增一次,即一回合完成;
// 超出步进预算视为演出队列卡死,fail-fast 不静默绕过(对应 M1.b 验收 3.e)。

using System;
using System.Security.Cryptography;
using System.Text;
using Sango.Core;

namespace Sango.Runtime
{
    public static class SangoTurnDriver
    {
        /// <summary>Entry 运行时默认种子(M1.b 固定;接 Web UI 设置是 M1.c 的事)。</summary>
        public const int DefaultSeed = 20260902;

        /// <summary>
        /// headless 虚拟帧长:计时型演出事件(DelayEvent/CameraMoveEvent 等)按
        /// "一拍完成"结算;不消耗模拟随机,故不影响确定性 digest。
        /// </summary>
        public const float VirtualFrameSeconds = 1_000_000f;

        // 一回合的 Run() 步进预算:42 势力 × 若干阶段 + 演出排空,量级远小于此;
        // 触顶说明 Run 停滞(演出事件等待玩家输入/协程),必须抛错示警。
        private const int MaxRunCallsPerTurn = 500_000;

        private static int _forceTurnsStarted;
        private static int _forceAiRuns;
        private static bool _playerControlObserved;

        static SangoTurnDriver()
        {
            GameEvent.OnForceTurnStart -= OnForceTurnStart;
            GameEvent.OnForceTurnStart += OnForceTurnStart;
            GameEvent.OnForceAIStart -= OnForceAIStart;
            GameEvent.OnForceAIStart += OnForceAIStart;
            // 玩家回合阻塞位(Corps.Run 对君主亲领军团触发后返回 false,见 SangoPlayerTurnOps
            // 文件头);置旗供 AdvanceTurn 识别"世界在等玩家"并停在该点。
            GameEvent.OnPlayerControl -= OnPlayerControl;
            GameEvent.OnPlayerControl += OnPlayerControl;
        }

        /// <summary>最近一次 AdvanceTurn 期间开始行动的势力数(MUD 摘要用)。</summary>
        public static int LastTurnForceCount { get; private set; }

        /// <summary>最近一次 AdvanceTurn 期间跑完 AI 的势力数(MUD 摘要用)。</summary>
        public static int LastTurnAiCount { get; private set; }

        /// <summary>AdvanceTurn 的终态:回合完整走完,或停在玩家回合等待行动。</summary>
        public enum TurnAdvanceResult
        {
            /// <summary>turnCount 已 +1(全托管世界的唯一终态;玩家局中玩家势力灭亡后同此)。</summary>
            TurnCompleted,

            /// <summary>世界停在本玩家回合的君主军团阻塞位(玩家下令后需 EndPlayerTurn 放行)。</summary>
            AwaitingPlayer,
        }

        /// <summary>
        /// 推进一个 sango 回合(全部势力按队列行动 + 旬/月结算)。全托管世界:走到
        /// turnCount+1;玩家局:跨过回合边界后继续推进到下一回合的玩家阻塞位(玩家势力
        /// 在 MakeForceQuene 排最前,阻塞即下一回合的起点;玩家势力已灭亡则退化为全托管
        /// 终态)。两种终态都入命令 journal(kind=step,含推进后 WorldDigest——重放侧逐
        /// 回合比对的期望值真源)。
        /// </summary>
        public static TurnAdvanceResult AdvanceTurn()
        {
            Scenario scenario = Scenario.Cur
                ?? throw new InvalidOperationException("Sango kernel is not booted; call SangoKernelBoot.Boot first.");

            int targetTurn = scenario.Info.turnCount + 1;
            int forcesBefore = _forceTurnsStarted;
            int aiBefore = _forceAiRuns;
            bool playerInPlay = SangoPlayerTurnOps.PlayerForceAlive(scenario);

            UnityEngine.Time.deltaTime = VirtualFrameSeconds;
            for (int i = 0; i < MaxRunCallsPerTurn; i++)
            {
                if (_playerControlObserved)
                {
                    _playerControlObserved = false;
                    LastTurnForceCount = _forceTurnsStarted - forcesBefore;
                    LastTurnAiCount = _forceAiRuns - aiBefore;
                    SangoCommandJournal.Record(
                        SangoReplayJournal.StepKind, SangoStepArgs.Instance, digestAfter: WorldDigest());
                    return TurnAdvanceResult.AwaitingPlayer;
                }

                scenario.Run();
                if (scenario.Info.turnCount >= targetTurn && !playerInPlay)
                {
                    LastTurnForceCount = _forceTurnsStarted - forcesBefore;
                    LastTurnAiCount = _forceAiRuns - aiBefore;
                    SangoCommandJournal.Record(
                        SangoReplayJournal.StepKind, SangoStepArgs.Instance, digestAfter: WorldDigest());
                    return TurnAdvanceResult.TurnCompleted;
                }
            }

            throw new InvalidOperationException(
                $"sango turn {targetTurn} stalled after {MaxRunCallsPerTurn} Run() calls " +
                $"(turnCount={scenario.Info.turnCount}, curForce={scenario.Info.curForceId} {scenario.Info.curForceName}); " +
                "the render-event gate is likely waiting on player input or a Unity coroutine.\n" +
                Sango.Render.RenderEvent.Instance.Dump());
        }

        /// <summary>MUD 风格单行回合摘要(id/年月日/事件计数)。</summary>
        public static string DescribeTurn()
        {
            Scenario scenario = Scenario.Cur
                ?? throw new InvalidOperationException("Sango kernel is not booted.");
            return $"[turn {scenario.Info.turnCount,3}] {scenario.GetDateStr()} | " +
                   $"势力行动 {LastTurnForceCount} | AI 结算 {LastTurnAiCount} | " +
                   $"存活势力 {CountAlive(scenario.forceSet.Count, i => scenario.forceSet[i] != null && scenario.forceSet[i].IsAlive)} / {scenario.forceSet.Count}";
        }

        /// <summary>
        /// 世界态确定性指纹:全城市 (id, 金, 粮, 人口, 耐久, 归属, 名单指纹) + 全武将
        /// (id, loyalty, 所属势力, state) + 全部队 (id, corpsId, forceId, cell x/y, 兵力)
        /// 升序拼接后的 SHA-256。同种子重放必须逐位相等(对齐 deterministic_replay 验收)。
        /// 城域行双源(D-1'):SangoCityNativeRuntime 挂载时读组件源(GAS 属性+保序组件,
        /// 对拍后正式源);未挂载读内核源——两源同格式,对拍 = 同种子同命令流下逐位相等。
        /// 武将行双源(D-2'):SangoPersonNativeRuntime 挂载且为当前世界权威时读组件源
        /// (GAS 忠诚 + 归属/状态组件,读缝同步后产出);未挂载读内核源。行格式与两源
        /// 对照合同同城域。部队行语义:编成/移动/消灭都改变该行;无任务部队逐回合仅
        /// 耗粮不动(id/corps/force/cell/兵力恒定),行稳定。武将行读对象引用真源
        /// (mBelongForce?.Id):内核运行时改归属只动引用、序列化 int 字段(BelongForce)
        /// 由 OnScenarioSave 存档时点才回写——活世界的 int 是脏值,捕获回灌链会把它
        /// 归一,读 int 会让"存档续跑 == 直跑"出现假性分叉(M3.a 内政 AI 活化后武将
        /// 流动常态化,该语义差异进入 digest 面);武将组件的归属引用同步(BelongForceId)
        /// 同读活引用真源。
        /// </summary>
        public static string WorldDigest()
        {
            Scenario scenario = Scenario.Cur
                ?? throw new InvalidOperationException("Sango kernel is not booted.");
            var rows = new System.Collections.Generic.List<string>();
            if (SangoCityNativeRuntime.Active is { IsDisposed: false } native && native.IsCurrentKernel)
            {
                rows.AddRange(native.CityDigestRows());
            }
            else
            {
                rows.AddRange(SangoCityNativeRuntime.KernelCityRows(scenario));
            }

            if (SangoPersonNativeRuntime.Active is { IsDisposed: false } nativePersons && nativePersons.IsCurrentKernel)
            {
                rows.AddRange(nativePersons.PersonDigestRows());
            }
            else
            {
                rows.AddRange(SangoPersonNativeRuntime.KernelPersonRows(scenario));
            }

            scenario.troopsSet.ForEach(troop =>
            {
                if (troop != null && troop.IsAlive)
                    rows.Add($"troop {troop.Id}:{troop.mBelongCorps?.Id ?? 0}:{troop.mBelongForce?.Id ?? 0}:{troop.x}:{troop.y}:{troop.troops}");
            });
            rows.Sort(StringComparer.Ordinal);

            var builder = new StringBuilder();
            foreach (string row in rows)
            {
                builder.Append(row).Append('\n');
            }

            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));
            return Convert.ToHexString(hash);
        }

        static void OnForceTurnStart(Force force, Scenario scenario) => _forceTurnsStarted++;

        static void OnForceAIStart(Force force, Scenario scenario) => _forceAiRuns++;

        static void OnPlayerControl(Corps corps, Scenario scenario) => _playerControlObserved = true;

        static int CountAlive(int count, Func<int, bool> isAlive)
        {
            int alive = 0;
            for (int i = 0; i < count; i++)
            {
                if (isAlive(i))
                    alive++;
            }
            return alive;
        }
    }
}
