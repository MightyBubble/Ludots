using Arch.Core;
using Arch.System;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS;

namespace Ludots.Core.Gameplay.GAS.Systems
{
    /// <summary>
    /// TagRuleSet系统（在Phase 0 SchemaUpdate阶段注册TagRuleSet）
    /// </summary>
    public class TagRuleSetSystem : BaseSystem<World, float>
    {
        private readonly TagOps _tagOps;

        public TagRuleSetSystem(World world, TagOps tagOps) : base(world)
        {
            _tagOps = tagOps ?? throw new System.ArgumentNullException(nameof(tagOps));
        }
        
        public override void Update(in float dt)
        {
            // Phase 0 SchemaUpdate阶段：注册TagRuleSet
            // 未来可以扩展为从配置文件加载TagRuleSet
        }
        
        /// <summary>
        /// 注册TagRuleSet（供外部调用）
        /// </summary>
        public void RegisterTagRuleSet(int tagId, TagRuleSet ruleSet)
        {
            _tagOps.RegisterTagRuleSet(tagId, ruleSet);
        }
    }
}
