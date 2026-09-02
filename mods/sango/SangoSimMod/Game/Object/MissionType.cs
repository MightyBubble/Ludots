namespace Sango.Core
{
    public enum MissionType : int
    {
        None = 0,
        TroopReturnCity,
        TroopMovetoCity,
        TroopDestroyTroop,
        TroopDestroyBuilding,
        TroopOccupyCity,
        TroopHarassCity,
        TroopBanishTroop,
        TroopProtectBuilding,
        TroopProtectTroop,
        TroopProtectCity,
        TroopBuildBuilding,
        TroopFixBuilding,
        TroopTransformGoodsToCity,
        TroopStay,

        PersonBuild,
        PersonCreateBoat,
        PersonCreateMachine,
        PersonWork,
        PersonInTroop,
        PersonResearch,

        /// <summary>
        /// 移动
        /// </summary>
        PersonTransform,

        /// <summary>
        /// 返回所在城市
        /// </summary>
        PersonReturn,

        /// <summary>
        /// 招募
        /// </summary>
        PersonRecruitPerson,

        /// <summary>
        /// 外交任务
        /// </summary>
        PersonDiplomacy,


        TroopMovetoCell,
        TroopMovetoBuild,

    }
}
