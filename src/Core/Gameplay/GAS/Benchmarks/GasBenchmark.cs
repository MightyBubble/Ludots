using System;
using System.Diagnostics;
using Arch.Core;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.GAS.Systems;
using Ludots.Core.Physics;
using Ludots.Core.Gameplay.GAS; // Added for GameplayEventBus

namespace Ludots.Core.Gameplay.GAS.Benchmarks
{
    public class GasBenchmark
    {
        public static void Run()
        {
            Console.WriteLine("Initializing GAS Benchmark (Abilities & Hooks)...");

            // 1. Setup Registry
            int healthId = AttributeRegistry.Register("Health");
            int manaId = AttributeRegistry.Register("Mana");
            int damageEventId = TagRegistry.Register("Event.DamageTaken");
            
            // 2. Setup World
            var world = World.Create();
            var physicsWorld = new PhysicsWorld();
            var eventBus = new GameplayEventBus(); // Initialize EventBus

            var effectTemplates = new EffectTemplateRegistry();
            var effectRequests = new EffectRequestQueue(initialCapacity: 65536);
            var clock = new DiscreteClock();
            var clocks = new GasClocks(clock);
            var conditions = new GasConditionRegistry();

            var mods = new EffectModifiers();
            mods.Add(healthId, ModifierOp.Add, 5.0f);
            effectTemplates.Register(1, new EffectTemplateData
            {
                TagId = 0,
                LifetimeKind = EffectLifetimeKind.Instant,
                ClockId = GasClockId.FixedFrame,
                DurationTicks = 0,
                PeriodTicks = 0,
                ExpireCondition = default,
                ParticipatesInResponse = true,
                Modifiers = mods
            });
            
            // 3. Create Systems
            var appSystem = new EffectApplicationSystem(world, effectRequests);
            var durSystem = new EffectLifetimeSystem(world, clock, conditions, effectRequests);
            var aggSystem = new AttributeAggregatorSystem(world);

            var proposalSystem = new EffectProposalProcessingSystem(world, effectRequests, null, effectTemplates);
            var abilitySystem = new AbilitySystem(world, effectRequests);
            var reactionSystem = new ReactionSystem(world, abilitySystem, eventBus);
            
            // 4. Create Global Ability Template (Flyweight)
            var abilityTemplateEntity = world.Create();
            world.Add(abilityTemplateEntity, new AbilityTemplate());
            world.Add(abilityTemplateEntity, new AbilityOnActivateEffects());
            unsafe
            {
                ref var onActivate = ref world.Get<AbilityOnActivateEffects>(abilityTemplateEntity);
                onActivate.Add(1);
            }

            // 5. Create Entities
            int entityCount = 100_000;
            Console.WriteLine($"Creating {entityCount} entities with Abilities...");
            var entities = new Entity[entityCount];
            
            var archetype = new ComponentType[] { 
                typeof(AttributeBuffer), 
                typeof(ActiveEffectContainer),
                typeof(GameplayTagContainer),
                typeof(TagCountContainer),
                typeof(AbilityStateBuffer),
                typeof(ReactionBuffer)
            };
            
            for (int i = 0; i < entityCount; i++)
            {
                var e = world.Create(archetype);
                entities[i] = e;
                
                ref var attr = ref world.Get<AttributeBuffer>(e);
                attr.SetBase(healthId, 100f);
                attr.SetBase(manaId, 100f); 
                attr.SetCurrent(healthId, 100f);
                attr.SetCurrent(manaId, 100f);
                
                ref var abilities = ref world.Get<AbilityStateBuffer>(e);
                abilities.AddAbility(abilityTemplateEntity); 
                
                ref var reactions = ref world.Get<ReactionBuffer>(e);
                reactions.Add(damageEventId, 0);
            }
            
            Console.WriteLine("Entities created. Warming up GC...");
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            
            long initialMemory = GC.GetTotalMemory(true);
            
            // 6. Run Loop
            Console.WriteLine("Running 100 frames with Event Spawning...");
            Stopwatch sw = Stopwatch.StartNew();
            long startAlloc = GC.GetAllocatedBytesForCurrentThread();
            
            var source = world.Create();
            
            for (int i = 0; i < 100; i++)
            {
                float dt = 0.016f;
                
                for(int j=0; j<1000; j++)
                {
                    eventBus.Publish(new GameplayEvent 
                    { 
                        TagId = damageEventId,
                        Source = source,
                        Target = entities[j] 
                    });
                }
                
                eventBus.Update(); // Update Bus

                reactionSystem.Update(dt); // Drives AbilitySystem
                abilitySystem.Update(dt);  // Process deferred structural changes
                proposalSystem.Update(dt);
                appSystem.Update(dt);
                clocks.AdvanceFixedFrame();
                clocks.AdvanceStep();
                durSystem.Update(dt);
                aggSystem.Update(dt);
            }
            
            long endAlloc = GC.GetAllocatedBytesForCurrentThread();
            sw.Stop();
            
            long diffAlloc = endAlloc - startAlloc;
            
            Console.WriteLine($"Benchmark Complete.");
            Console.WriteLine($"Time: {sw.ElapsedMilliseconds} ms");
            Console.WriteLine($"GC Allocated: {diffAlloc} bytes (Expected near 0)");
            Console.WriteLine($"FPS: {1000.0f / (sw.ElapsedMilliseconds / 100.0f)}");

            World.Destroy(world);
        }
    }
}
