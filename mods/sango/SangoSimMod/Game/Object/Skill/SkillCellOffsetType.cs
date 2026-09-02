namespace Sango.Core
{

    public enum SkillCellOffsetType : int
    {
        None = 0,
        /// <summary>
        /// 施法者按施法者->目标方向位移,直到移不动
        /// </summary>
        Master = 1,

        /// <summary>
        /// 目标按施法者->目标方向位移,直到移不动
        /// </summary>
        Target = 2,

        /// <summary>
        /// 施法者按施法者->目标方向位移,直到移不动,并对阻挡者进行碰撞检测
        /// </summary>
        MasterBlock = 3,

        /// <summary>
        /// 目标按施法者->目标方向位移,直到移不动,并对阻挡者进行碰撞检测
        /// </summary>
        TargetBlock = 4,

        /// <summary>
        /// 施法者从最末开始往初始位置找,找到第一个落脚点,位移至此
        /// </summary>
        MasterJustCheckEnd = 5,

        /// <summary>
        /// 施法者随机位移到周围可移动的格子
        /// </summary>
        MasterRandom = 6,

        /// <summary>
        /// 目标随机位移到周围可移动的格子
        /// </summary>
        TargetRandom = 7,

        /// <summary>
        /// 施法者位移到指定位置
        /// </summary>
        Master指定位置 = 8,

        /// <summary>
        /// 目标位移到指定位置
        /// </summary>
        Target指定位置 = 9
    }
}
