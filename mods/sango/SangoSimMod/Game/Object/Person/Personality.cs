
using TKNewtonsoft.Json;

namespace Sango.Core
{
    public class Personality : SangoObject
    {
        public int kind;

        /// <summary>
        /// 作为目标对[伪报]成功率加成
        /// </summary>
        public int falseReportSuccessAdd;

        /// <summary>
        /// 作为目标对[扰乱]成功率加成
        /// </summary>
        public int disturbSuccessAdd;

        /// <summary>
        /// 作为目标对[镇静]成功率加成
        /// </summary>
        public int calmdownSuccessAdd;

        /// <summary>
        /// 作为目标对[伏兵]成功率加成
        /// </summary>
        public int ambushSuccessAdd;

        /// <summary>
        /// 作为目标对[妖术]成功率加成
        /// </summary>
        public int sorcerySuccessAdd;

        /// <summary>
        /// 作为目标对[内讧]成功率加成
        /// </summary>
        public int infightingSuccessAdd;

        /// <summary>
        /// 作为主将释放[伪报]的暴击率加成
        /// </summary>
        public int falseReportCriticalAdd;

        /// <summary>
        /// 作为主将释放[扰乱]的暴击率加成
        /// </summary>
        public int disturbCriticalAdd;

        ///// <summary>
        ///// 作为主将释放[内讧]的暴击率加成
        ///// </summary>
        //public int infightingCriticalAdd;

        /// <summary>
        /// 战争倾向加成
        /// </summary>
        public int warTendencyAdd;

        /// <summary>
        /// 防御倾向加成
        /// </summary>
        public int defenseTendencyAdd;

        /// <summary>
        /// 外交倾向加成
        /// </summary>
        public int diplomacyTendencyAdd;

        /// <summary>
        /// 经济发展倾向加成
        /// </summary>
        public int economicTendencyAdd;

        /// <summary>
        /// 科技研发倾向加成
        /// </summary>
        public int technologyTendencyAdd;

        /// <summary>
        /// 俘虏招降倾向加成
        /// </summary>
        public int recruitCaptiveTendencyAdd;

        /// <summary>
        /// 俘虏释放倾向加成
        /// </summary>
        public int releaseCaptiveTendencyAdd;

        /// <summary>
        /// 俘虏赎回倾向加成
        /// </summary>
        public int ransomCaptiveTendencyAdd;

        /// <summary>
        /// 送礼效果加成（百分比）
        /// </summary>
        public int giftEffectAdd;

    }
}
