/*
 * 文件名：DebateSkill.cs
 * 描述：舌战技能类，用于表示舌战中的技能卡片
 * 创建日期：2026-03-27
 * 最后修改：2026-03-27
 */

using System;

namespace Sango.Core.Debate
{
    /// <summary>
    /// 舌战技能类型
    /// </summary>
    public enum DebateSkillType
    {
        Attack,    // 攻击技能
        Defense,   // 防御技能
        Support,   // 支援技能
        Special    // 特殊技能
    }

    /// <summary>
    /// 舌战技能效果类型
    /// </summary>
    public enum DebateSkillEffectType
    {
        Damage,        // 造成伤害
        Heal,          // 恢复士气
        Buff,          // 增益效果
        Debuff,        // 减益效果
        SpecialEffect  // 特殊效果
    }

    /// <summary>
    /// 舌战技能
    /// </summary>
    public class DebateSkill
    {
        /// <summary>
        /// 技能ID
        /// </summary>
        public int ID { get; set; }

        /// <summary>
        /// 技能名称
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// 技能类型
        /// </summary>
        public DebateSkillType Type { get; set; }

        /// <summary>
        /// 技能等级
        /// </summary>
        public int Level { get; set; }

        /// <summary>
        /// 技能效果类型
        /// </summary>
        public DebateSkillEffectType EffectType { get; set; }

        /// <summary>
        /// 技能效果值
        /// </summary>
        public int EffectValue { get; set; }

        /// <summary>
        /// 技能描述
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// 是否为特殊技能
        /// </summary>
        public bool IsSpecial { get; set; }

        /// <summary>
        /// 技能动画效果
        /// </summary>
        public string AnimationEffect { get; set; }

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="id">技能ID</param>
        /// <param name="name">技能名称</param>
        /// <param name="type">技能类型</param>
        /// <param name="level">技能等级</param>
        /// <param name="effectType">技能效果类型</param>
        /// <param name="effectValue">技能效果值</param>
        /// <param name="description">技能描述</param>
        /// <param name="isSpecial">是否为特殊技能</param>
        public DebateSkill(int id, string name, DebateSkillType type, int level, DebateSkillEffectType effectType, int effectValue, string description, bool isSpecial = false)
        {
            ID = id;
            Name = name;
            Type = type;
            Level = level;
            EffectType = effectType;
            EffectValue = effectValue;
            Description = description;
            IsSpecial = isSpecial;
        }

        /// <summary>
        /// 执行技能效果
        /// </summary>
        /// <param name="user">技能使用者</param>
        /// <param name="target">技能目标</param>
        public void Execute(DebateParticipant user, DebateParticipant target)
        {
            switch (EffectType)
            {
                case DebateSkillEffectType.Damage:
                    target.ReduceMorale(EffectValue);
                    break;
                case DebateSkillEffectType.Heal:
                    user.AddMorale(EffectValue);
                    break;
                case DebateSkillEffectType.Buff:
                    // 实现增益效果
                    break;
                case DebateSkillEffectType.Debuff:
                    // 实现减益效果
                    break;
                case DebateSkillEffectType.SpecialEffect:
                    // 实现特殊效果
                    break;
            }
        }
    }

    /// <summary>
    /// 舌战技能工厂
    /// </summary>
    public static class DebateSkillFactory
    {
        /// <summary>
        /// 技能ID计数器
        /// </summary>
        private static int _skillIdCounter = 1;

        /// <summary>
        /// 初始化技能工厂
        /// </summary>
        public static void Init()
        {
            _skillIdCounter = 1;
        }

        /// <summary>
        /// 创建技能
        /// </summary>
        /// <param name="type">技能类型</param>
        /// <param name="level">技能等级</param>
        /// <returns>创建的技能</returns>
        public static DebateSkill CreateSkill(DebateSkillType type, int level)
        {
            int id = _skillIdCounter++;
            string name = "";
            DebateSkillEffectType effectType = DebateSkillEffectType.Damage;
            int effectValue = 0;
            string description = "";

            switch (type)
            {
                case DebateSkillType.Attack:
                    name = GetAttackSkillName(level);
                    effectType = DebateSkillEffectType.Damage;
                    effectValue = 10 * level;
                    description = $"对对手造成{effectValue}点士气伤害";
                    break;
                case DebateSkillType.Defense:
                    name = GetDefenseSkillName(level);
                    effectType = DebateSkillEffectType.Heal;
                    effectValue = 8 * level;
                    description = $"恢复{effectValue}点士气";
                    break;
                case DebateSkillType.Support:
                    name = GetSupportSkillName(level);
                    effectType = DebateSkillEffectType.Buff;
                    effectValue = 5 * level;
                    description = "增加自身防御力";
                    break;
                case DebateSkillType.Special:
                    name = GetSpecialSkillName();
                    effectType = DebateSkillEffectType.SpecialEffect;
                    effectValue = 15;
                    description = "特殊效果";
                    break;
            }

            return new DebateSkill(id, name, type, level, effectType, effectValue, description);
        }

        /// <summary>
        /// 创建特殊技能
        /// </summary>
        /// <returns>创建的特殊技能</returns>
        public static DebateSkill CreateSpecialSkill()
        {
            int id = _skillIdCounter++;
            string[] specialSkillNames = { "舌战群儒", "雄辩", "妙语连珠", "口若悬河", "铁齿铜牙" };
            string name = specialSkillNames[UnityEngine.Random.Range(0, specialSkillNames.Length)];

            return new DebateSkill(id, name, DebateSkillType.Special, 3, DebateSkillEffectType.SpecialEffect, 20, "强大的特殊效果", true);
        }

        /// <summary>
        /// 获取攻击技能名称
        /// </summary>
        /// <param name="level">技能等级</param>
        /// <returns>攻击技能名称</returns>
        private static string GetAttackSkillName(int level)
        {
            string[] names = { "质疑", "反驳", "批判", "抨击", "驳斥" };
            return names[UnityEngine.Random.Range(0, names.Length)] + level;
        }

        /// <summary>
        /// 获取防御技能名称
        /// </summary>
        /// <param name="level">技能等级</param>
        /// <returns>防御技能名称</returns>
        private static string GetDefenseSkillName(int level)
        {
            string[] names = { "辩解", "解释", "澄清", "辩护", "申辩" };
            return names[UnityEngine.Random.Range(0, names.Length)] + level;
        }

        /// <summary>
        /// 获取支援技能名称
        /// </summary>
        /// <param name="level">技能等级</param>
        /// <returns>支援技能名称</returns>
        private static string GetSupportSkillName(int level)
        {
            string[] names = { "举例", "引用", "类比", "比喻", "例证" };
            return names[UnityEngine.Random.Range(0, names.Length)] + level;
        }

        /// <summary>
        /// 获取特殊技能名称
        /// </summary>
        /// <returns>特殊技能名称</returns>
        private static string GetSpecialSkillName()
        {
            string[] names = { "舌战群儒", "雄辩", "妙语连珠", "口若悬河", "铁齿铜牙" };
            return names[UnityEngine.Random.Range(0, names.Length)];
        }
    }
}
