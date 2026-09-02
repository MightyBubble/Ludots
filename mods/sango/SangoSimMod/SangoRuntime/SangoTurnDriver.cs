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

        static SangoTurnDriver()
        {
            GameEvent.OnForceTurnStart -= OnForceTurnStart;
            GameEvent.OnForceTurnStart += OnForceTurnStart;
            GameEvent.OnForceAIStart -= OnForceAIStart;
            GameEvent.OnForceAIStart += OnForceAIStart;
        }

        /// <summary>最近一次 AdvanceTurn 期间开始行动的势力数(MUD 摘要用)。</summary>
        public static int LastTurnForceCount { get; private set; }

        /// <summary>最近一次 AdvanceTurn 期间跑完 AI 的势力数(MUD 摘要用)。</summary>
        public static int LastTurnAiCount { get; private set; }

        /// <summary>推进一个 sango 回合(全部势力按队列行动 + 旬/月结算)。</summary>
        public static void AdvanceTurn()
        {
            Scenario scenario = Scenario.Cur
                ?? throw new InvalidOperationException("Sango kernel is not booted; call SangoKernelBoot.Boot first.");

            int targetTurn = scenario.Info.turnCount + 1;
            int forcesBefore = _forceTurnsStarted;
            int aiBefore = _forceAiRuns;

            UnityEngine.Time.deltaTime = VirtualFrameSeconds;
            for (int i = 0; i < MaxRunCallsPerTurn; i++)
            {
                scenario.Run();
                if (scenario.Info.turnCount >= targetTurn)
                {
                    LastTurnForceCount = _forceTurnsStarted - forcesBefore;
                    LastTurnAiCount = _forceAiRuns - aiBefore;
                    return;
                }
            }

            throw new InvalidOperationException(
                $"sango turn {targetTurn} stalled after {MaxRunCallsPerTurn} Run() calls " +
                $"(turnCount={scenario.Info.turnCount}, curForce={scenario.Info.curForceId} {scenario.Info.curForceName}); " +
                "the render-event gate is likely waiting on player input or a Unity coroutine.");
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
        /// 世界态确定性指纹:全城市 (id, gold, food) + 全武将 (id, loyalty, BelongForce, state)
        /// + 全部队 (id, corpsId, forceId, cell x/y, 兵力) 升序拼接后的 SHA-256。同种子重放
        /// 必须逐位相等(对齐 deterministic_replay 验收)。部队行语义:编成/移动/消灭都改变
        /// 该行;无任务部队逐回合仅耗粮不动(id/corps/force/cell/兵力恒定),行稳定。
        /// 武将行是换种子差异的落点:上游快照的 City.AIPrepare 内政指令被整段注释,
        /// 城市金粮在短期内只走俸给/军粮公式(与随机无关);种子差异由武将层
        /// (忠诚流动/官职/状态)承接,M1.c 重启内政 AI 后城市行自然加入差异面。
        /// </summary>
        public static string WorldDigest()
        {
            Scenario scenario = Scenario.Cur
                ?? throw new InvalidOperationException("Sango kernel is not booted.");
            var rows = new System.Collections.Generic.List<string>();
            scenario.citySet.ForEach(city =>
            {
                if (city != null)
                    rows.Add($"city {city.Id}:{city.gold}:{city.food}");
            });
            scenario.personSet.ForEach(person =>
            {
                if (person != null)
                    rows.Add($"person {person.Id}:{person.loyalty}:{person.BelongForce}:{person.state}");
            });
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
