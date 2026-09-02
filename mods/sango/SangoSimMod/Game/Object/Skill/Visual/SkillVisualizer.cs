using System.Collections.Generic;
using UnityEngine;

namespace Sango.Core
{
    public abstract class SkillVisualizer
    {
        public SkillInstance skillInstance;
        public virtual void Init(SkillInstance skillInstance) { this.skillInstance = skillInstance; }
        public abstract void PlaySkillVisual(Troop troop, Cell spellCell, List<Cell> atkCellList);
        public virtual void StopSkillVisual() { }

        public delegate SkillVisualizer SkillVisualizerCreator();

        public static Dictionary<string, SkillVisualizerCreator> CreateMap = new Dictionary<string, SkillVisualizerCreator>();
        public static void Register(string name, SkillVisualizerCreator creator)
        {
            CreateMap[name] = creator;
        }
        public static SkillVisualizer CreateHandle<T>() where T : SkillVisualizer, new()
        {
            return new T();
        }
        public static SkillVisualizer Create(string name)
        {
            SkillVisualizerCreator creator;
            if (CreateMap.TryGetValue(name, out creator))
                return creator();
            return null;
        }

        public static void Init()
        {
            Register("Default", CreateHandle<DefaultSkillVisualizer>);
            Register("Range", CreateHandle<RangeSkillVisualizer>);
            Register("Melee", CreateHandle<MeleeSkillVisualizer>);
            Register("Strategy", CreateHandle<StrategySkillVisualizer>);
        }
    }

    public class DefaultSkillVisualizer : SkillVisualizer
    {
        public override void PlaySkillVisual(Troop troop, Cell spellCell, List<Cell> atkCellList)
        {
            // 默认技能视觉效果
            troop.Render.SetAniShow(1);
            troop.Render.FaceTo(spellCell.Position);
        }
    }

    public class RangeSkillVisualizer : SkillVisualizer
    {
        public override void PlaySkillVisual(Troop troop, Cell spellCell, List<Cell> atkCellList)
        {
            // 远程技能视觉效果
            troop.Render.SetAniShow(1, true);
            troop.Render.FaceTo(spellCell.Position);
            // 发射箭头特效
            troop.Render.CastArrow(spellCell.Position);
        }
    }

    public class MeleeSkillVisualizer : SkillVisualizer
    {
        public override void PlaySkillVisual(Troop troop, Cell spellCell, List<Cell> atkCellList)
        {
            // 近战技能视觉效果
            troop.Render.SetAniShow(2);
            troop.Render.FaceTo(spellCell.Position);
        }
    }

    public class StrategySkillVisualizer : SkillVisualizer
    {
        public override void PlaySkillVisual(Troop troop, Cell spellCell, List<Cell> atkCellList)
        {
            // 计策技能视觉效果
            troop.Render.SetAniShow(3);
            troop.Render.FaceTo(spellCell.Position);
            // 计策特效
            foreach (var cell in atkCellList)
            {
                // 在每个目标格子上显示计策特效
                EffectManager.Instance.PlayEffect("StrategyEffect", cell.Position);
            }
        }
    }
}
