namespace Sango.Core
{
    /// <summary>
    /// 部队属性
    /// </summary>
    public class TroopAttribute
    {
        public Troop Troop { get; set; }

        /// <summary>
        /// 兵刃攻击
        /// </summary>
        public int bladeAttack;

        /// <summary>
        /// 兵刃防御
        /// </summary>
        public int bladeDefence;

        /// <summary>
        /// 兵刃额外伤害倍率
        /// </summary>
        public int extraBladeMultiplier;

        /// <summary>
        /// 谋略攻击
        /// </summary>
        public int strategicAttack;

        /// <summary>
        /// 谋略防御
        /// </summary>
        public int strategicDefence;

        /// <summary>
        /// 谋略额外伤害倍率
        /// </summary>
        public int extraStrategicMultiplier;

        /// <summary>
        /// 最终伤害倍率
        /// </summary>
        public int finalMultiplier;

        /// <summary>
        /// 命中率
        /// </summary>
        public int hit;

        /// <summary>
        /// 闪避率
        /// </summary>
        public int miss;

        /// <summary>
        /// 忽略闪避率
        /// </summary>
        public int ignoreMiss;

        /// <summary>
        /// 暴击率
        /// </summary>
        public int critical;

        /// <summary>
        /// 抗暴
        /// </summary>
        public int criticalResistance;

        /// <summary>
        /// 格挡率
        /// </summary>
        public int block;

        /// <summary>
        /// 格挡伤害比率
        /// </summary>
        public int blockResist;

    }
}
