using Arch.Core;
using Arch.System;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Registry;

namespace Ludots.Core.Gameplay.GAS.Systems
{
    public class DeferredTriggerProcessSystem : BaseSystem<World, float>
    {
        private readonly DeferredTriggerQueue _triggerQueue;
        private readonly GameplayEventBus? _eventBus;
        
        public DeferredTriggerProcessSystem(World world, DeferredTriggerQueue triggerQueue, GameplayEventBus? eventBus = null) : base(world)
        {
            _triggerQueue = triggerQueue;
            _eventBus = eventBus;
        }
        
        public override void Update(in float dt)
        {
            for (int i = 0; i < _triggerQueue.AttributeTriggerCount; i++)
            {
                var trigger = _triggerQueue.GetAttributeTrigger(i);
                ProcessAttributeChangedTrigger(trigger);
            }
            
            for (int i = 0; i < _triggerQueue.TagTriggerCount; i++)
            {
                var trigger = _triggerQueue.GetTagTrigger(i);
                ProcessTagChangedTrigger(trigger);
            }
            
            for (int i = 0; i < _triggerQueue.TagCountTriggerCount; i++)
            {
                var trigger = _triggerQueue.GetTagCountTrigger(i);
                ProcessTagCountChangedTrigger(trigger);
            }
            
            // 清空队列（下一帧会重新填充）
            _triggerQueue.Clear();
        }
        
        private void ProcessAttributeChangedTrigger(AttributeChangedTrigger trigger)
        {
            if (!World.IsAlive(trigger.Target))
            {
                return;
            }
            
            if (_eventBus == null) return;
            int eventTagId = AttributeEventTagRegistry.GetEventTagId(trigger.AttributeId);
            if (eventTagId == TagRegistry.InvalidId) return;
            _eventBus.Publish(new GameplayEvent
            {
                TagId = eventTagId,
                Source = trigger.Target,
                Target = trigger.Target,
                Magnitude = trigger.NewValue
            });
        }
        
        private void ProcessTagChangedTrigger(TagChangedTrigger trigger)
        {
            if (!World.IsAlive(trigger.Target))
            {
                return;
            }
            
            if (_eventBus == null) return;
            float magnitude = trigger.IsPresent ? 1f : -1f;
            _eventBus.Publish(new GameplayEvent
            {
                TagId = trigger.TagId,
                Source = trigger.Target,
                Target = trigger.Target,
                Magnitude = magnitude
            });
        }
        
        private void ProcessTagCountChangedTrigger(TagCountChangedTrigger trigger)
        {
            if (!World.IsAlive(trigger.Target))
            {
                return;
            }
            
            if (_eventBus == null) return;
            _eventBus.Publish(new GameplayEvent
            {
                TagId = trigger.TagId,
                Source = trigger.Target,
                Target = trigger.Target,
                Magnitude = trigger.NewCount
            });
        }
    }
}
