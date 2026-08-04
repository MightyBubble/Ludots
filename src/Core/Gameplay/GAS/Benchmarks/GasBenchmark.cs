using System;
using System.Diagnostics;
using Arch.Core;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Input;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.GAS.Systems;
using Ludots.Core.Physics;
using Ludots.Core.Gameplay.GAS; // Added for GameplayEventBus

namespace Ludots.Core.Gameplay.GAS.Benchmarks
{
    public class GasBenchmark
    {
        private static readonly ResponseChainOrderTypes BenchmarkResponseChainOrderTypes = new ResponseChainOrderTypes
        {
            ChainPass = 1,
            ChainNegate = 2,
            ChainActivateEffect = 3
        };

        public static void Run()
        {
            const int entityCount = 100_000;
            Console.WriteLine("Initializing GAS Benchmark (Abilities & Hooks)...");

            // 1. Setup Registry
            int healthId = AttributeRegistry.Register("Health");
            int manaId = AttributeRegistry.Register("Mana");
            int damageEventId = TagRegistry.Register("Event.DamageTaken");
            const int castAbilityOrderTypeId = 100;
            
            // 2. Setup World
            var world = World.Create();
            var physicsWorld = new PhysicsWorld();
            var eventBus = new GameplayEventBus(); // Initialize EventBus

            var effectTemplates = new EffectTemplateRegistry();
            var effectRequests = new EffectRequestQueue(initialCapacity: 65536);
            var clock = new DiscreteClock();
            var clocks = new GasClocks(clock);
            var conditions = new GasConditionRegistry();
            var tagOps = new TagOps(new DirtyEntityQueue(entityCount), new TagRuleRegistry());
            var terminalResults = new OrderTerminalResultBuffer(capacity: 65536);
            var admissionResults = new OrderAdmissionResultBuffer(capacity: 65536, rejectionCapacity: 65536);
            var orderQueue = new OrderQueue(capacity: 65536, admissionResults);
            var orderTypes = new OrderTypeRegistry(terminalResults);
            orderTypes.Register(new OrderTypeConfig
            {
                Key = "castAbility",
                OrderTypeId = castAbilityOrderTypeId,
                AllowQueuedMode = false,
                CanInterruptSelf = true,
                ClearQueueOnActivate = true,
                IntArg0BlackboardKey = OrderBlackboardKeys.Cast_SlotIndex,
                EntityBlackboardKey = OrderBlackboardKeys.Cast_TargetEntity,
                SpatialBlackboardKey = -1,
            });

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
            var appSystem = new EffectApplicationSystem(world, fanOutCommandCapacity: 65536, clock, effectRequests, tagOps: tagOps);
            var durSystem = new EffectLifetimeSystem(world, clock, conditions, snapshotCapacity: 4096, fanOutCommandCapacity: 65536, effectRequests: effectRequests, tagOps: tagOps);
            var aggSystem = new AttributeAggregatorSystem(world, tagOps: tagOps);

            var proposalSystem = new EffectProposalProcessingSystem(
                world,
                effectRequests,
                fanOutCommandCapacity: 65536,
                clock,
                templates: effectTemplates,
                responseChainOrderTypes: BenchmarkResponseChainOrderTypes,
                tagOps: tagOps);
            var orderBufferSystem = new OrderBufferSystem(
                world,
                clock,
                orderTypes,
                new OrderRuleRegistry(),
                admissionResults,
                orderQueue,
                closeEntityIntakeOnUpdate: false);
            var abilityExecSystem = new AbilityExecSystem(
                world,
                clock,
                new InputRequestQueue(),
                new InputResponseBuffer(),
                effectRequests,
                snapshotCapacity: 4096,
                eventBus: eventBus,
                castAbilityOrderTypeId: castAbilityOrderTypeId,
                tagOps: tagOps,
                orderTypeRegistry: orderTypes);
            var reactionSystem = new ReactionSystem(world, orderQueue, castAbilityOrderTypeId, eventBus);
            
            // 4. Create Global Ability Template (Flyweight)
            var abilityTemplateEntity = world.Create();
            world.Add(abilityTemplateEntity, new AbilityTemplate());
            var execSpec = default(AbilityExecSpec);
            execSpec.ClockId = GasClockId.Step;
            execSpec.SetItem(0, ExecItemKind.EffectSignal, tick: 0, templateId: 1);
            execSpec.SetItem(1, ExecItemKind.End, tick: 0);
            world.Add(abilityTemplateEntity, execSpec);

            // 5. Create Entities
            Console.WriteLine($"Creating {entityCount} entities with Abilities...");
            var entities = new Entity[entityCount];
            
            var archetype = new ComponentType[] { 
                typeof(AttributeBuffer), 
                typeof(ActiveEffectContainer),
                typeof(GameplayTagContainer),
                typeof(TagCountContainer),
                typeof(DirtyFlags),
                typeof(AbilityStateBuffer),
                typeof(ReactionBuffer),
                typeof(OrderBuffer),
                typeof(BlackboardIntBuffer),
                typeof(BlackboardEntityBuffer)
            };
            
            for (int i = 0; i < entityCount; i++)
            {
                var e = world.Create(archetype);
                entities[i] = e;
                world.Set(e, OrderBuffer.CreateEmpty());
                
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

                terminalResults.Clear();
                admissionResults.BeginLogicStep();
                reactionSystem.Update(dt);
                orderBufferSystem.Update(dt);
                abilityExecSystem.Update(dt);
                admissionResults.EndEntityIntake();
                proposalSystem.Update(dt);
                appSystem.Update(dt);
                clocks.AdvanceFixedFrame();
                clocks.AdvanceStep();
                durSystem.Update(dt);
                aggSystem.Update(dt);
                admissionResults.EndLogicStep();
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
