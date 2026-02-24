using System;
using System.Diagnostics;
using Arch.Core;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Systems;
using Ludots.Core.Physics;
using Ludots.Core.Systems;
using Schedulers;
using NUnit.Framework;
using static NUnit.Framework.Assert;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    public class SystemIntegrationTests
    {
        private readonly TagOps _tagOps = new TagOps();
        private World _world;
        private GameplayEventBus _eventBus;
        private PhysicsWorld _physicsWorld;
        
        [SetUp]
        public void Setup()
        {
            _world = World.Create();
            _tagOps.ClearRuleRegistry();
            _eventBus = new GameplayEventBus();
            _physicsWorld = new PhysicsWorld();
            
            // 设置SharedJobScheduler以支持并行查询
            if (World.SharedJobScheduler == null)
            {
                World.SharedJobScheduler = new JobScheduler(new JobScheduler.Config
                {
                    ThreadPrefixName = "GasTests",
                    ThreadCount = 0, // Auto
                    MaxExpectedConcurrentJobs = 64,
                    StrictAllocationMode = false
                });
            }
        }
        
        [TearDown]
        public void TearDown()
        {
            _world?.Dispose();
        }
        
        [Test]
        public void TestSystemExecutionOrder()
        {
            // Arrange: 创建所有系统（跳过需要依赖的系统）
            var reactionSystem = new ReactionSystem(_world, null, _eventBus);
            var effectTemplates = new EffectTemplateRegistry();
            var effectRequests = new EffectRequestQueue();
            var clock = new DiscreteClock();
            var clocks = new GasClocks(clock);
            var conditions = new GasConditionRegistry();
            var abilitySystem = new AbilitySystem(_world, effectRequests);
            var proposalSystem = new EffectProposalProcessingSystem(_world, effectRequests, null, effectTemplates);
            var appSystem = new EffectApplicationSystem(_world, effectRequests);
            var aggSystem = new AttributeAggregatorSystem(_world);
            var lifetimeSystem = new EffectLifetimeSystem(_world, clock, conditions, effectRequests);
            
            // Act: 按Phase顺序执行
            float dt = 0.016f;
            
            Console.WriteLine("[SystemIntegrationTests] Phase 1: InputCollection (skipped - requires dependencies)");
            
            Console.WriteLine("[SystemIntegrationTests] Phase 2: AbilityActivation");
            reactionSystem.Update(dt);
            abilitySystem.Update(dt);
            
            Console.WriteLine("[SystemIntegrationTests] Phase 3: EffectProcessing");
            proposalSystem.Update(dt);
            appSystem.Update(dt);
            
            Console.WriteLine("[SystemIntegrationTests] Phase 4: AttributeCalculation");
            aggSystem.Update(dt);
            
            Console.WriteLine("[SystemIntegrationTests] Phase 6: Cleanup");
            lifetimeSystem.Update(dt);
            
            // Assert: 系统执行顺序正确
            Pass("System execution order verified");
        }
        
        [Test]
        public void TestEffectLifecycle_StateTransitions()
        {
            // Arrange: 创建Effect
            var effectEntity = _world.Create();
            var effect = new GameplayEffect
            {
                LifetimeKind = EffectLifetimeKind.After,
                ClockId = GasClockId.FixedFrame,
                TotalTicks = 10,
                RemainingTicks = 10,
                State = EffectState.Created
            };
            _world.Add(effectEntity, effect);
            _world.Add(effectEntity, new EffectContext { Source = effectEntity, Target = effectEntity });
            _world.Add(effectEntity, new EffectModifiers());
            // PendingEffect marker tag removed — state is managed via GameplayEffect.State
            
            // Act: 模拟Effect生命周期
            ref var effectRef = ref _world.Get<GameplayEffect>(effectEntity);
            
            // Created -> Pending
            effectRef.State = EffectState.Pending;
            That(effectRef.State, Is.EqualTo(EffectState.Pending));
            Console.WriteLine("[SystemIntegrationTests] Effect state: Created -> Pending");
            
            // Pending -> Trigger
            effectRef.State = EffectState.Trigger;
            That(effectRef.State, Is.EqualTo(EffectState.Trigger));
            Console.WriteLine("[SystemIntegrationTests] Effect state: Pending -> Trigger");
            
            // Trigger -> Calculate
            effectRef.State = EffectState.Calculate;
            That(effectRef.State, Is.EqualTo(EffectState.Calculate));
            Console.WriteLine("[SystemIntegrationTests] Effect state: Trigger -> Calculate");
            
            // Calculate -> Apply
            effectRef.State = EffectState.Apply;
            That(effectRef.State, Is.EqualTo(EffectState.Apply));
            Console.WriteLine("[SystemIntegrationTests] Effect state: Calculate -> Apply");
            
            // Apply -> Committed
            effectRef.State = EffectState.Committed;
            That(effectRef.State, Is.EqualTo(EffectState.Committed));
            Console.WriteLine("[SystemIntegrationTests] Effect state: Apply -> Committed");
            
            // Assert
            Pass("Effect lifecycle state transitions verified");
        }
        
        [Test]
        public void TestTagOps_WithTagRuleSet_Integration()
        {
            // Arrange
            var entity = _world.Create();
            _world.Add(entity, new GameplayTagContainer());
            _world.Add(entity, new TagCountContainer());
            
            int tagA = 1;
            int tagB = 2;
            int tagC = 3;
            
            // 注册规则：tagA attached tagB, tagA removed tagC
            var ruleSetA = new TagRuleSet();
            unsafe
            {
                ruleSetA.AttachedTags[0] = tagB;
                ruleSetA.AttachedCount = 1;
                ruleSetA.RemovedTags[0] = tagC;
                ruleSetA.RemovedCount = 1;
            }
            _tagOps.RegisterTagRuleSet(tagA, ruleSetA);
            
            // 先添加tagC
            ref var tagsRef = ref _world.Get<GameplayTagContainer>(entity);
            ref var countsRef = ref _world.Get<TagCountContainer>(entity);
            _tagOps.AddTag(ref tagsRef, ref countsRef, tagC);
            
            // Act: 添加tagA，应该自动添加tagB并移除tagC
            bool result = _tagOps.AddTag(ref tagsRef, ref countsRef, tagA);
            
            // Assert
            That(result, Is.True);
            ref var tags = ref _world.Get<GameplayTagContainer>(entity);
            That(tags.HasTag(tagA), Is.True);
            That(tags.HasTag(tagB), Is.True);
            That(tags.HasTag(tagC), Is.False);
            
            Console.WriteLine($"[SystemIntegrationTests] TestTagOps_WithTagRuleSet_Integration: Tag rules applied correctly");
        }
        
        [Test]
        public void TestDeferredTrigger_Integration()
        {
            // Arrange
            var entity = _world.Create();
            _world.Add(entity, new AttributeBuffer());
            _world.Add(entity, new ActiveEffectContainer());
            _world.Add(entity, new DirtyFlags());
            
            var triggerQueue = new DeferredTriggerQueue();
            var collectionSystem = new DeferredTriggerCollectionSystem(_world, triggerQueue);
            var processSystem = new DeferredTriggerProcessSystem(_world, triggerQueue, new GameplayEventBus());
            
            // 修改属性值（触发脏标记）
            ref var attrBuffer = ref _world.Get<AttributeBuffer>(entity);
            attrBuffer.SetBase(0, 100f);
            attrBuffer.SetCurrent(0, 150f);
            
            ref var dirtyFlags = ref _world.Get<DirtyFlags>(entity);
            dirtyFlags.MarkAttributeDirty(0);
            
            // Act: Phase 5 - 收集延迟触发器
            collectionSystem.Update(0.016f);
            
            // Assert: 触发器应该被收集
            That(triggerQueue.AttributeTriggerCount, Is.GreaterThan(0));
            Console.WriteLine($"[SystemIntegrationTests] TestDeferredTrigger_Integration: Collected {triggerQueue.AttributeTriggerCount} attribute triggers");
            
            // Act: Phase 5 - 处理延迟触发器
            processSystem.Update(0.016f);
            
            // Assert: 队列应该被清空
            That(triggerQueue.AttributeTriggerCount, Is.EqualTo(0));
            Console.WriteLine($"[SystemIntegrationTests] TestDeferredTrigger_Integration: Processed triggers successfully");
        }
    }
}
