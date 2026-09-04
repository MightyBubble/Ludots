/*
 * 文件名：DebateParticipant.cs
 * 描述：舌战参与者类，用于表示舌战中的双方角色
 * 创建日期：2026-03-27
 * 最后修改：2026-03-27
 */

using System;
using System.Collections.Generic;

namespace Sango.Core.Debate
{
    /// <summary>
    /// 舌战参与者类型
    /// </summary>
    public enum ParticipantType
    {
        Player,  // 玩家
        AI       // AI
    }

    /// <summary>
    /// 舌战参与者
    /// </summary>
    public class DebateParticipant
    {
        /// <summary>
        /// 参与者ID
        /// </summary>
        public int ID { get; set; }

        /// <summary>
        /// 参与者名称
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// 参与者类型
        /// </summary>
        public ParticipantType Type { get; set; }

        /// <summary>
        /// 士气值（0-100）
        /// </summary>
        public int Morale { get; set; }

        /// <summary>
        /// 愤怒值（0-100）
        /// </summary>
        public int Anger { get; set; }

        /// <summary>
        /// 当前技能卡片
        /// </summary>
        public List<DebateSkill> Skills { get; private set; }

        /// <summary>
        /// 智力值
        /// </summary>
        public int Intelligence { get; set; }

        /// <summary>
        /// 魅力值
        /// </summary>
        public int Charisma { get; set; }

        /// <summary>
        /// 舌战技能列表
        /// </summary>
        public List<DebateSkill> AvailableSkills { get; private set; }

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="id">参与者ID</param>
        /// <param name="name">参与者名称</param>
        /// <param name="type">参与者类型</param>
        /// <param name="intelligence">智力值</param>
        /// <param name="charisma">魅力值</param>
        public DebateParticipant(int id, string name, ParticipantType type, int intelligence, int charisma)
        {
            ID = id;
            Name = name;
            Type = type;
            Intelligence = intelligence;
            Charisma = charisma;
            Morale = 100; // 初始士气值
            Anger = 0;    // 初始愤怒值
            Skills = new List<DebateSkill>();
            AvailableSkills = new List<DebateSkill>();
        }

        /// <summary>
        /// 初始化技能卡片
        /// </summary>
        public void InitSkills()
        {
            // 清空现有技能
            Skills.Clear();
            AvailableSkills.Clear();

            // 根据智力和魅力生成技能卡片
            GenerateSkills();
        }

        /// <summary>
        /// 生成技能卡片
        /// </summary>
        private void GenerateSkills()
        {
            // 生成基础技能
            for (int i = 0; i < 5; i++)
            {
                // 随机生成技能类型
                DebateSkillType skillType = (DebateSkillType)UnityEngine.Random.Range(0, 4);
                int skillLevel = CalculateSkillLevel();

                DebateSkill skill = DebateSkillFactory.CreateSkill(skillType, skillLevel);
                if (skill != null)
                {
                    Skills.Add(skill);
                }
            }

            // 生成特殊技能
            if (UnityEngine.Random.value > 0.5f)
            {
                DebateSkill specialSkill = DebateSkillFactory.CreateSpecialSkill();
                if (specialSkill != null)
                {
                    Skills.Add(specialSkill);
                }
            }
        }

        /// <summary>
        /// 计算技能等级
        /// </summary>
        /// <returns>技能等级</returns>
        private int CalculateSkillLevel()
        {
            // 根据智力和魅力计算技能等级
            int baseLevel = Intelligence / 20;
            int bonusLevel = Charisma / 30;
            int totalLevel = baseLevel + bonusLevel;

            // 确保技能等级在1-3之间
            return Math.Max(1, Math.Min(3, totalLevel));
        }

        /// <summary>
        /// 使用技能
        /// </summary>
        /// <param name="skillIndex">技能索引</param>
        /// <returns>使用的技能</returns>
        public DebateSkill UseSkill(int skillIndex)
        {
            if (skillIndex >= 0 && skillIndex < Skills.Count)
            {
                DebateSkill skill = Skills[skillIndex];
                Skills.RemoveAt(skillIndex);
                return skill;
            }
            return null;
        }

        /// <summary>
        /// 补充技能卡片
        /// </summary>
        public void RefillSkills()
        {
            // 当技能卡片少于3张时补充
            while (Skills.Count < 3)
            {
                DebateSkillType skillType = (DebateSkillType)UnityEngine.Random.Range(0, 4);
                int skillLevel = CalculateSkillLevel();

                DebateSkill skill = DebateSkillFactory.CreateSkill(skillType, skillLevel);
                if (skill != null)
                {
                    Skills.Add(skill);
                }
            }
        }

        /// <summary>
        /// 增加士气值
        /// </summary>
        /// <param name="value">增加的值</param>
        public void AddMorale(int value)
        {
            Morale = Math.Min(100, Morale + value);
        }

        /// <summary>
        /// 减少士气值
        /// </summary>
        /// <param name="value">减少的值</param>
        public void ReduceMorale(int value)
        {
            Morale = Math.Max(0, Morale - value);
        }

        /// <summary>
        /// 增加愤怒值
        /// </summary>
        /// <param name="value">增加的值</param>
        public void AddAnger(int value)
        {
            Anger = Math.Min(100, Anger + value);
        }

        /// <summary>
        /// 重置愤怒值
        /// </summary>
        public void ResetAnger()
        {
            Anger = 0;
        }

        /// <summary>
        /// 检查是否战败
        /// </summary>
        /// <returns>是否战败</returns>
        public bool IsDefeated()
        {
            return Morale <= 0;
        }
    }
}
