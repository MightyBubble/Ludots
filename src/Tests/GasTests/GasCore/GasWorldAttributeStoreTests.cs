using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.GAS.Systems;
using Ludots.Core.Registry;
using NUnit.Framework;
using static NUnit.Framework.Assert;

namespace GasTests.GasCore
{
    /// <summary>
    /// RFC-0067 P1 验收：属性容量随本局内容定容，槽位 [64, Plan) 全链可用
    /// （注册 → 种子/变异 → 聚合 → 延迟触发 → 读回），[0,64) 内嵌镜像与列存一致。
    /// </summary>
    [TestFixture]
    public sealed class GasWorldAttributeStoreTests
    {
        private TagOps _tagOps = null!;
        private World _world = null!;

        [SetUp]
        public void SetUp()
        {
            ModRegistryAmbient.Reset();
            var set = new ModRegistrySet();
            ModRegistryAmbient.Bind(set);
            for (int i = 0; i < 100; i++)
            {
                AttributeRegistry.Register($"test.high.attr.{i}");
            }

            That(AttributeRegistry.Count, Is.EqualTo(100));
            var plan = GasLoadTimeCapacityPlan.Freeze(
                AttributeRegistry.Count, TagRegistry.Count,
                physicalAttributeSlots: 1024, physicalTagIdSpace: 256);
            WorldAttributeStoreAmbient.Bind(new WorldAttributeStore(plan, rowCapacity: 128));

            _world = World.Create();
            _tagOps = new TagOps(
                new DirtyEntityQueue(4096),
                new TagRuleRegistry(),
                aggregateDirty: new AttributeAggregateDirtyRegistry());
        }

        [TearDown]
        public void TearDown()
        {
            WorldAttributeStoreAmbient.Reset();
            ModRegistryAmbient.Reset();
            _world?.Dispose();
        }

        private Entity CreateAttributeEntity()
        {
            return _world.Create(new AttributeBuffer(), new DirtyFlags(), new GameplayTagContainer());
        }

        [Test]
        public void HighSlots_RegisterWriteRead_RoundTripsThroughStore()
        {
            int idA = AttributeRegistry.RequireId("test.high.attr.70");
            int idB = AttributeRegistry.RequireId("test.high.attr.80");
            That(idA, Is.GreaterThanOrEqualTo(AttributeBuffer.MAX_ATTRS), "前置：第 71 个注册名落在高槽位");

            Entity entity = CreateAttributeEntity();
            AttributeMutationOps.SetBase(_world, entity, idA, 100f, _tagOps);
            AttributeMutationOps.SetCurrent(_world, entity, idB, 55f, _tagOps);

            That(AttributeReads.Base(_world, entity, idA), Is.EqualTo(100f));
            That(AttributeReads.Current(_world, entity, idA), Is.EqualTo(100f), "SetBase 同步落 current");
            That(AttributeReads.Current(_world, entity, idB), Is.EqualTo(55f));
            That(_world.Get<AttributeBuffer>(entity).HasAttribute(idA), Is.False, "高槽位不进内嵌镜像");
        }

        [Test]
        public void LowSlots_MirrorToStore_StaysConsistentWithEmbedded()
        {
            Entity entity = CreateAttributeEntity();
            AttributeMutationOps.SetBase(_world, entity, 70, 1f, _tagOps); // 建行
            AttributeMutationOps.SetBase(_world, entity, 5, 42f, _tagOps); // 低槽位 + 镜像

            var store = WorldAttributeStoreAmbient.Current!;
            That(store.TryGetRow(entity, out int row), Is.True);
            That(store.GetBase(row, 5), Is.EqualTo(42f));
            That(store.GetCurrent(row, 5), Is.EqualTo(_world.Get<AttributeBuffer>(entity).GetCurrent(5)));
        }

        [Test]
        public void HighSlot_Aggregation_ComputesCapFromModifiers()
        {
            int id = AttributeRegistry.RequireId("test.high.attr.70");
            Entity entity = _world.Create(
                new AttributeBuffer(), new DirtyFlags(), new GameplayTagContainer(),
                new ActiveEffectContainer());
            AttributeMutationOps.SetBase(_world, entity, id, 100f, _tagOps);

            var modifiers = new EffectModifiers();
            That(modifiers.Add(id, ModifierOp.Add, 10f), Is.True, "EffectModifiers 必须能承载高 id");
            Entity effect = _world.Create(
                new GameplayEffect { State = EffectState.Committed, AggregatesModifiers = true },
                modifiers);
            _world.Get<ActiveEffectContainer>(entity).Add(effect);

            using var aggregator = new AttributeAggregatorSystem(_world, tagOps: _tagOps, aggregateDirty: _tagOps.AggregateDirty);
            aggregator.Update(0f);

            var store = WorldAttributeStoreAmbient.Current!;
            store.TryGetRow(entity, out int row);
            That(store.GetCap(row, id), Is.EqualTo(110f), "聚合 cap = base + 修饰符");
            That(store.GetCurrent(row, id), Is.EqualTo(100f), "持久 current 不被聚合改写");
        }

        [Test]
        public void HighSlot_DeferredTrigger_EnqueuesAttributeChanged()
        {
            int id = AttributeRegistry.RequireId("test.high.attr.80");
            Entity entity = CreateAttributeEntity();
            AttributeMutationOps.SetCurrent(_world, entity, id, 55f, _tagOps);

            var triggerQueue = new DeferredTriggerQueue(64);
            var collection = new DeferredTriggerCollectionSystem(_world, triggerQueue, _tagOps, _tagOps.DirtyEntities);
            collection.Update(0f);
            That(CountTriggers(triggerQueue), Is.EqualTo(1), "变异种值首变 0→55 触发一次（与内嵌车道同语义）");
            triggerQueue.Clear();

            AttributeMutationOps.SetCurrent(_world, entity, id, 60f, _tagOps);
            collection.Update(0f);

            That(CountTriggers(triggerQueue), Is.EqualTo(1), "高槽位变更必须触发 AttributeChanged");
            bool sawHigh = false;
            for (int i = 0; i < triggerQueue.AttributeTriggerCount; i++)
            {
                AttributeChangedTrigger trigger = triggerQueue.GetAttributeTrigger(i);
                if (trigger.AttributeId == id && trigger.OldValue == 55f && trigger.NewValue == 60f)
                {
                    sawHigh = true;
                }
            }

            That(sawHigh, Is.True, "触发必须携带高槽位 id 与新旧值");
        }

        private static int CountTriggers(DeferredTriggerQueue queue)
        {
            return queue.AttributeTriggerCount;
        }

        [Test]
        public void Store_RowCapacity_FailClosedBeyondPrewrite()
        {
            var plan = GasLoadTimeCapacityPlan.Freeze(2, 1, 1024, 256);
            var tiny = new WorldAttributeStore(plan, rowCapacity: 2);
            var w = World.Create();
            var a = w.Create(new AttributeBuffer(), new DirtyFlags());
            var b = w.Create(new AttributeBuffer(), new DirtyFlags());
            tiny.EnsureRow(a);
            tiny.EnsureRow(b);
            InvalidOperationException ex = Throws<InvalidOperationException>(() => tiny.EnsureRow(w.Create()))!;
            That(ex.Message, Does.Contain("AttributeRowCapacityExceeded"));
            w.Dispose();
        }

        [Test]
        public void HighLane_WithoutStore_FailsClosed()
        {
            WorldAttributeStoreAmbient.Reset();
            Entity entity = CreateAttributeEntity();
            int id = AttributeRegistry.RequireId("test.high.attr.70");
            InvalidOperationException ex = Throws<InvalidOperationException>(() =>
                AttributeMutationOps.SetCurrent(_world, entity, id, 1f, _tagOps))!;
            That(ex.Message, Does.Contain("HighLaneUnavailable"));
        }
    }
}
