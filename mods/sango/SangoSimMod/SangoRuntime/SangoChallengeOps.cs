// M3.f 战斗演出系统(单挑/舌战)的触发接线。
// 逆向结论(上游 sango-src 25150a91 快照):
//   内核两套自洽、入口齐全、但上游从未接线——全仓搜 StartDuel/TriggerDebate/ProcessDuel/
//   OnDuelStart 的调用方,Duel 侧只有 Troop.StartDuel(Troop.cs:899)这个死入口与
//   GameEvent.OnDuelStart/End/DecisionRequired 三个零订阅事件;Debate 侧只有
//   DebateIntegration.TriggerDebate(自身无调用方)与 Game.cs 的 Init/Update 空转。
//   SkillInstance.Action/TroopAIUtility/TroopDestroyTroop 均无任何单挑/舌战分支
//   (技能表与 Data/*.json 也无一骑打/舌战技能行)。触发判定因此是移植侧补全的规则,
//   落点与语义按下述锚定:
//     触发环节 = SkillInstance.Action 的交锋幸存时刻(GameEvent.OnSkillDamageTroopAfter,
//       Action 在伤害+反击结算后、双方存活才发)——这是内核暴露的唯一"两军交锋仍在
//       继续"的结算内事件;空间门槛沿用 DuelSystem.CanStartDuel 自身的合同
//       (Map.Distance <= 1 + 双方 IsFight + 双方主将在场),不另发明条件;
//     演出类型 = 攻方主将属性定向(Strength >= Intelligence 走单挑,否则舌战)——与
//       DuelSystem.GetDefaultDecision 的属性决策逻辑同源;
//     概率 = ChallengeChancePercent(默认 5%,每次合格交锋一掷),GameRandom 种子化,
//       存档跨界的确定性由此成立(挑战全程同步结算,无跨帧状态);
//     玩家侧 = 原版是 window_debate/window UI 弹窗回合制(D8 不搬);headless 与
//       Web UI 交互面就绪前的取舍:自动托管——玩家侧参与者按内核 AI 的同一决策面
//       (随机话术/属性默认决策)代答,战报行进消息流留证。交互式 Web 演出窗是后续
//       表现层工作,不阻塞本接线。
//   单挑解算与结果回写(士气 ±20 直写、30% 俘将、20% 部队溃灭)全部是
//   DuelSystem/DuelManager 内核既有代码,本文件只调用不复制;舌战内核(Debate 族)
//   上游无任何结果回写(End 后只通知 UI),胜负面落点 ±10 士气(Troop.ChangeMorale,
//       走内核公开 API:夹取上限 + OnTroopChangeMorale 事件)是移植侧补全的唯一
//       新语义,数值取单挑内核 ±20 的一半量级。
// 重新入场说明:挑战在 OnSkillDamageTroopAfter 回调内同步结算(与该事件群的既有
//   OverrideData 回调同属性);单挑 20% 溃灭走 Troop.Clear()(幂等、置 IsAlive=false、
//   发 OnTroopClear),Action 循环后续分支都有 IsAlive 门,标记投影按 OnTroopClear 摘除。

using System;
using Sango.Core;
using Sango.Core.Debate;
using Sango.Core.Tools;

namespace Sango.Runtime
{
    /// <summary>战斗演出的类型(单挑 = 武力对拼,舌战 = 话术对拼)。</summary>
    public enum SangoChallengeKind
    {
        Duel,
        Debate,
    }

    public static class SangoChallengeOps
    {
        /// <summary>
        /// 每次合格交锋触发演出的概率(百分比)。默认 5:每次邻格交锋一掷,
        /// 既有会战节奏(每部队每回合一次主动技能 + 反击不触发)下约为数十回合一遇。
        /// </summary>
        public static int ChallengeChancePercent = 5;

        /// <summary>
        /// 演出类型覆写(验收播种用测试缝,同 GameSystemManager.debug 性质);
        /// null = 按攻方主将属性定向。运行时保持 null。
        /// </summary>
        public static SangoChallengeKind? ForcedKind;

        private static bool _attached;

        /// <summary>
        /// 舌战结果行(内核舌战事件是实例事件,非静态 GameEvent;战报经
        /// SangoCombatAnnals 订阅本事件汇入同一消息流)。
        /// </summary>
        public static event Action<string>? DebateLinePublished;

        /// <summary>订阅交锋幸存事件(幂等;内核二次启动无需重挂,事件是静态委托)。</summary>
        public static void Attach()
        {
            if (_attached)
            {
                return;
            }

            GameEvent.OnSkillDamageTroopAfter -= OnExchangeSurvived;
            GameEvent.OnSkillDamageTroopAfter += OnExchangeSurvived;
            _attached = true;
        }

        public static void Detach()
        {
            if (!_attached)
            {
                return;
            }

            GameEvent.OnSkillDamageTroopAfter -= OnExchangeSurvived;
            _attached = false;
        }

        static void OnExchangeSurvived(SkillInstance skill, Troop defender, OverrideData<int> damage)
        {
            if (skill?.master == null || defender == null)
            {
                return;
            }

            Troop attacker = skill.master;
            if (!attacker.IsAlive || !defender.IsAlive || !attacker.IsFight || !defender.IsFight)
            {
                return;
            }

            if (attacker.Leader == null || defender.Leader == null)
            {
                return;
            }

            // DuelSystem.CanStartDuel 的空间合同:邻格交锋才可能走演出。
            if (Scenario.Cur?.Map.Distance(attacker.cell, defender.cell) > 1)
            {
                return;
            }

            if (!GameRandom.Chance(ChallengeChancePercent))
            {
                return;
            }

            SangoChallengeKind kind = ForcedKind ??
                (attacker.Leader.Strength >= attacker.Leader.Intelligence
                    ? SangoChallengeKind.Duel
                    : SangoChallengeKind.Debate);

            if (kind == SangoChallengeKind.Duel)
            {
                // Troop.StartDuel → DuelManager(内含 CanStartDuel 全量门)→ ProcessDuel
                // 解算至终局(内核 HandleDuelResult 落士气/俘斩/溃灭)。
                if (attacker.StartDuel(defender))
                {
                    DuelManager.Instance.ProcessDuel();
                }

                return;
            }

            RunDebate(attacker, defender);
        }

        // 舌战:参与者取双方主将(智力/魅力面),攻守类型按势力归属定——原版
        // TriggerDebate 把 person1 硬编码为 Player(玩家发起语境);战斗语境对称,
        // 玩家势力一侧保留 Player 类型,由本托管面代答,行为与 AI 同源。
        static void RunDebate(Troop attacker, Troop defender)
        {
            var participant1 = new DebateParticipant(
                attacker.Leader!.Id, attacker.Leader.Name,
                attacker.IsPlayer ? ParticipantType.Player : ParticipantType.AI,
                attacker.Leader.Intelligence, attacker.Leader.Glamour);
            var participant2 = new DebateParticipant(
                defender.Leader!.Id, defender.Leader.Name,
                defender.IsPlayer ? ParticipantType.Player : ParticipantType.AI,
                defender.Leader.Intelligence, defender.Leader.Glamour);

            DebateLinePublished?.Invoke(
                $"[舌战] {TroopLeaderLabel(attacker)}(士气 {attacker.morale}) 与 " +
                $"{TroopLeaderLabel(defender)}(士气 {defender.morale}) 阵前论战");

            DebateManager.Instance.StartDebate(participant1, participant2);
            DebateInstance? debate = DebateManager.Instance.GetCurrentDebate();
            if (debate == null)
            {
                return;
            }

            HostPlayerTurns(debate);

            // 胜负面落点:胜方部队 +10 士气,败方 -10,平局不动(单挑内核 ±20 的一半量级)。
            // D-4':写入改走 GAS 激活(SangoCombatNativeRuntime.ResolveChallengeMorale →
            // Ability.Sango.Challenge.DebateWin/Lose → 士气步骤);未挂载时退化为纯内核写
            // (写面惯例,语义与内核逐位同)。
            switch (debate.Result)
            {
                case DebateResult.Participant1Win:
                    ApplyDebateMorale(attacker, defender);
                    break;
                case DebateResult.Participant2Win:
                    ApplyDebateMorale(defender, attacker);
                    break;
            }

            DebateLinePublished?.Invoke(DebateEndLine(attacker, defender, debate.Result));
        }
        static void ApplyDebateMorale(Troop winner, Troop loser)
        {
            if (SangoCombatNativeRuntime.Active is { IsDisposed: false, IsCurrentKernel: true })
            {
                SangoCombatNativeRuntime.Active.ResolveChallengeMorale(winner, win: true);
                SangoCombatNativeRuntime.Active.ResolveChallengeMorale(loser, win: false);
                return;
            }

            winner.ChangeMorale(10);
            loser.ChangeMorale(-10);
        }

        // 玩家侧托管:轮到 Player 参与者时按内核 AI 的同一决策面(随机话术)代答。
        static void HostPlayerTurns(DebateInstance debate)
        {
            // 每次代答推进一整轮(双方各一手);内核胜负界是 50 轮,超界即视为状态机异常。
            const int maxPlayerPicks = 128;
            for (int pick = 0; pick < maxPlayerPicks && debate.IsRunning; pick++)
            {
                DebateParticipant current = debate.CurrentTurnParticipant;
                if (current.Type != ParticipantType.AI)
                {
                    debate.UseSkill(current, GameRandom.Range(0, current.Skills.Count));
                }
            }

            if (debate.IsRunning)
            {
                throw new InvalidOperationException(
                    "debate instance stalled in the player-hosting loop; the kernel 50-round bound should have ended it.");
            }
        }

        static string TroopLeaderLabel(Troop troop) =>
            $"{troop.mBelongForce?.Name ?? "无主"}·{troop.Leader?.Name ?? "未知"}({troop.Name})";

        static string DebateEndLine(Troop attacker, Troop defender, DebateResult result)
        {
            string tail = $"攻军士气 {attacker.morale},守军士气 {defender.morale}";
            switch (result)
            {
                case DebateResult.Participant1Win:
                    return $"[舌战·终] {TroopLeaderLabel(attacker)} 驳倒 {TroopLeaderLabel(defender)},{tail}";
                case DebateResult.Participant2Win:
                    return $"[舌战·终] {TroopLeaderLabel(defender)} 驳倒 {TroopLeaderLabel(attacker)},{tail}";
                default:
                    return $"[舌战·终] {TroopLeaderLabel(attacker)} 与 {TroopLeaderLabel(defender)} 不分高下,{tail}";
            }
        }
    }
}
