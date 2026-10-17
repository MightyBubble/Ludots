using Arch.Core;
using Arch.Core.Extensions;
using Arch.Buffer;
using Ludots.Core.Gameplay.GAS.Components;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Ludots.Core.Gameplay.GAS.Systems
{
    public class ReactionSystem : BaseSystem<World, float>
    {
        private AbilitySystem _abilitySystem;
        private GameplayEventBus _eventBus;
        
        // Initial capacity sized for high-throughput frames (benchmark: 1000 events/frame);
        // 1 event -> 1 reaction, so capacity must be >= the event count.
        private readonly List<Activation> _activations = new(4096);

        public ReactionSystem(World world, AbilitySystem abilitySystem, GameplayEventBus eventBus) : base(world) 
        {
            _abilitySystem = abilitySystem;
            _eventBus = eventBus;
        }

        public override unsafe void Update(in float dt)
        {
            var events = _eventBus.Events;
            _activations.Clear();

            // Direct iteration is optimal: O(events), with a small inner loop
            // (reactions per entity, usually < 5).

            for (int i = 0; i < events.Count; i++)
            {
                var evt = events[i];
                
                // Has + Get (not TryGet): we need a ref; TryGet would return a copy of the ReactionBuffer struct.
                if (World.IsAlive(evt.Target) && World.Has<ReactionBuffer>(evt.Target))
                {
                    ref var reactions = ref World.Get<ReactionBuffer>(evt.Target);
                    
                    for (int j = 0; j < reactions.Count; j++)
                    {
                        if (reactions.EventTagIds[j] == evt.TagId)
                        {
                            _activations.Add(new Activation { Caster = evt.Target, SlotIndex = reactions.AbilitySlots[j], Source = evt.Source });
                        }
                    }
                }
            }

            for (int i = 0; i < _activations.Count; i++)
            {
                var activation = _activations[i];
                _abilitySystem.TryActivateAbility(activation.Caster, activation.SlotIndex, activation.Source);
            }
        }

        private struct Activation
        {
            public Entity Caster;
            public int SlotIndex;
            public Entity Source;
        }
    }
}
