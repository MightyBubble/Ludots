using Arch.Core;
using Arch.System;
using Ludots.Core.Gameplay.GAS;

namespace Ludots.Core.Gameplay.GAS.Systems
{
    /// <summary>
    /// 属性Schema更新系统（Phase 0 SchemaUpdate）
    /// 处理ExtensionAttribute注册请求
    /// </summary>
    public class AttributeSchemaUpdateSystem : BaseSystem<World, float>
    {
        private readonly ExtensionAttributeRegistry _registry;
        private readonly AttributeSchemaUpdateQueue _updateQueue;
        
        public AttributeSchemaUpdateSystem(World world, ExtensionAttributeRegistry registry, AttributeSchemaUpdateQueue updateQueue) 
            : base(world)
        {
            _registry = registry;
            _updateQueue = updateQueue;
        }
        
        public override void Update(in float dt)
        {
            // Phase 0 SchemaUpdate阶段：处理ExtensionAttribute注册请求
            var pendingRegistrations = _updateQueue.GetPendingRegistrations();
            
            foreach (var fullName in pendingRegistrations)
            {
                _registry.Register(fullName);
            }
            
            // 清空队列
            _updateQueue.Clear();
        }
        
        /// <summary>
        /// 请求注册ExtensionAttribute（供外部调用）
        /// </summary>
        public void RequestRegistration(string fullName)
        {
            _updateQueue.EnqueueRegistration(fullName);
        }
    }
}
