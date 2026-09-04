/*
 * 文件名：DebateInstance.cs
 * 描述：舌战实例类，用于管理单个舌战的流程
 * 创建日期：2026-03-27
 * 最后修改：2026-03-27
 */

using System;
using System.Collections.Generic;

namespace Sango.Core.Debate
{
    /// <summary>
    /// 舌战状态
    /// </summary>
    public enum DebateState
    {
        Ready,     // 准备中
        Running,   // 进行中
        Ended      // 已结束
    }

    /// <summary>
    /// 舌战结果
    /// </summary>
    public enum DebateResult
    {
        None,      // 无结果
        Participant1Win,  // 参与者1胜利
        Participant2Win,  // 参与者2胜利
        Draw       // 平局
    }

    /// <summary>
    /// 舌战实例
    /// </summary>
    public class DebateInstance
    {
        /// <summary>
        /// 参与者1
        /// </summary>
        public DebateParticipant Participant1 { get; private set; }

        /// <summary>
        /// 参与者2
        /// </summary>
        public DebateParticipant Participant2 { get; private set; }

        /// <summary>
        /// 当前回合
        /// </summary>
        public int CurrentRound { get; private set; }

        /// <summary>
        /// 当前行动的参与者
        /// </summary>
        public DebateParticipant CurrentTurnParticipant { get; private set; }

        /// <summary>
        /// 舌战状态
        /// </summary>
        public DebateState State { get; private set; }

        /// <summary>
        /// 舌战结果
        /// </summary>
        public DebateResult Result { get; private set; }

        /// <summary>
        /// 是否正在运行
        /// </summary>
        public bool IsRunning { get { return State == DebateState.Running; } }

        /// <summary>
        /// 舌战事件回调
        /// </summary>
        public event Action<DebateInstance> OnDebateStart;
        public event Action<DebateInstance> OnDebateEnd;
        public event Action<DebateInstance, int> OnRoundStart;
        public event Action<DebateInstance, DebateParticipant, DebateSkill> OnSkillUse;
        public event Action<DebateInstance, DebateParticipant> OnTurnStart;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="participant1">参与者1</param>
        /// <param name="participant2">参与者2</param>
        public DebateInstance(DebateParticipant participant1, DebateParticipant participant2)
        {
            Participant1 = participant1;
            Participant2 = participant2;
            CurrentRound = 0;
            State = DebateState.Ready;
            Result = DebateResult.None;
        }

        /// <summary>
        /// 开始舌战
        /// </summary>
        public void Start()
        {
            // 初始化参与者技能
            Participant1.InitSkills();
            Participant2.InitSkills();

            // 设置初始回合
            CurrentRound = 1;
            CurrentTurnParticipant = Participant1; // 参与者1先手

            // 开始舌战
            State = DebateState.Running;

            // 触发舌战开始事件
            OnDebateStart?.Invoke(this);

            // 开始第一回合
            StartRound();
        }

        /// <summary>
        /// 结束舌战
        /// </summary>
        public void End()
        {
            State = DebateState.Ended;

            // 触发舌战结束事件
            OnDebateEnd?.Invoke(this);
        }

        /// <summary>
        /// 开始回合
        /// </summary>
        private void StartRound()
        {
            // 触发回合开始事件
            OnRoundStart?.Invoke(this, CurrentRound);

            // 开始当前参与者的回合
            StartTurn(CurrentTurnParticipant);
        }

        /// <summary>
        /// 开始回合
        /// </summary>
        /// <param name="participant">当前行动的参与者</param>
        private void StartTurn(DebateParticipant participant)
        {
            // 触发回合开始事件
            OnTurnStart?.Invoke(this, participant);

            // 补充技能卡片
            participant.RefillSkills();

            // 如果是AI，自动选择技能
            if (participant.Type == ParticipantType.AI)
            {
                // 简单的AI决策逻辑
                int skillIndex = UnityEngine.Random.Range(0, participant.Skills.Count);
                UseSkill(participant, skillIndex);
            }
        }

        /// <summary>
        /// 使用技能
        /// </summary>
        /// <param name="participant">技能使用者</param>
        /// <param name="skillIndex">技能索引</param>
        public void UseSkill(DebateParticipant participant, int skillIndex)
        {
            if (State != DebateState.Running || CurrentTurnParticipant != participant)
                return;

            // 使用技能
            DebateSkill skill = participant.UseSkill(skillIndex);
            if (skill == null)
                return;

            // 确定技能目标
            DebateParticipant target = (participant == Participant1) ? Participant2 : Participant1;

            // 执行技能效果
            skill.Execute(participant, target);

            // 触发技能使用事件
            OnSkillUse?.Invoke(this, participant, skill);

            // 检查胜负
            if (CheckWinCondition())
            {
                End();
                return;
            }

            // 切换到下一个参与者
            CurrentTurnParticipant = (CurrentTurnParticipant == Participant1) ? Participant2 : Participant1;

            // 如果回到参与者1，进入下一回合
            if (CurrentTurnParticipant == Participant1)
            {
                CurrentRound++;
                StartRound();
            }
            else
            {
                StartTurn(CurrentTurnParticipant);
            }
        }

        /// <summary>
        /// 检查胜利条件
        /// </summary>
        /// <returns>是否结束舌战</returns>
        private bool CheckWinCondition()
        {
            if (Participant1.IsDefeated())
            {
                Result = DebateResult.Participant2Win;
                return true;
            }
            else if (Participant2.IsDefeated())
            {
                Result = DebateResult.Participant1Win;
                return true;
            }

            // 回合数限制
            if (CurrentRound > 50)
            {
                // 根据剩余士气判断胜负
                if (Participant1.Morale > Participant2.Morale)
                {
                    Result = DebateResult.Participant1Win;
                }
                else if (Participant2.Morale > Participant1.Morale)
                {
                    Result = DebateResult.Participant2Win;
                }
                else
                {
                    Result = DebateResult.Draw;
                }
                return true;
            }

            return false;
        }

        /// <summary>
        /// 更新舌战
        /// </summary>
        /// <param name="deltaTime">时间增量</param>
        public void Update(float deltaTime)
        {
            if (State != DebateState.Running)
                return;

            // 可以在这里添加一些动态效果或动画更新
        }
    }
}
