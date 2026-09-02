using TKNewtonsoft.Json.Linq;
using System.Collections.Generic;

namespace Sango.Core
{
    /// <summary>
    /// 条件基类，所有游戏中的条件判断都继承自此类
    /// </summary>
    public interface IConditionDatabase
    {
        SkillInstance ActionSkill { get; }
        SkillInstance TargetSkill { get; }
        Person ActionPerson { get; }
        Person TargetPerson { get; }
        Troop ActionTroop { get; }
        Troop TargetTroop { get; }
        Cell ActionCell { get; }
        Cell TargetCell { get; }
        City ActionCity { get; }
        City TargetCity { get; }
        Corps ActionCorps { get; }
        Corps TargetCorps { get; }
        Force ActionForce { get; }
        Force TargetForce { get; }
        object ActionObject { get; }
        object TargetObject { get; }
    }

}
