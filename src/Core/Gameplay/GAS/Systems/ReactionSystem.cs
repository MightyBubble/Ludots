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
        
        // Increased initial capacity for high-throughput scenarios (Benchmark: 1000 events/frame)
        // If 1 event -> 1 reaction, we need capacity >= event count.
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

            // Direct iteration is actually optimal for small N (N < 10000).
            // Complexity is O(Events). Inner loop is O(Reactions per Entity), usually very small (< 5).
            // The previous issue might be cache misses or repeated World.Has/Get calls.
            // Optimization: Skip World.IsAlive check if we trust the EventBus source (or check it once).
            // Optimization: Use `TryGet` to avoid double lookup (Has + Get).

            for (int i = 0; i < events.Count; i++)
            {
                var evt = events[i];
                
                // Optimized: Use Has + Get to get ref instead of TryGet which returns a copy
                // This avoids value type copying overhead for ReactionBuffer struct
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
