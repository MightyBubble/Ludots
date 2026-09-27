using System;
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
    /// RFC-0067 P2 验收：标签容量随本局内容定容，位 [256, Plan) 经 TagOps 全链可用
    ///（add/remove/has → 脏 → 延迟触发 TagChanged），[1,256) 内嵌镜像与列存一致；
    /// 高 id 规则声明失败关闭（规则引擎按 256 编译，P3 对齐面）。
    /// </summary>
    [TestFixture]
    public unsafe sealed class GasTagCapacityTests
    {
        private TagOps _tagOps = null!;
        private World _world = null!;

        [SetUp]
        public void SetUp()
        {
            ModRegistryAmbient.Reset();
            ModRegistryAmbient.Bind(new ModRegistrySet());
            for (int i = 0; i < 300; i++)
            {
                TagRegistry.Register($"test.high.tag.{i}");
            }

            That(TagRegistry.Count, Is.EqualTo(300));
            var plan = GasLoadTimeCapacityPlan.Freeze(
                AttributeRegistry.Count, TagRegistry.Count,
                physicalAttributeSlots: 1024, physicalTagIdSpace: 4096);
            That(plan.TagIdSpace, Is.GreaterThanOrEqualTo(301), "位空间按字对齐容纳 300 标签");
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

        private unsafe Entity CreateTagEntity()
        {
            return _world.Create(new GameplayTagContainer(), new TagCountContainer(), new DirtyFlags());
        }

        [Test]
        public void HighTag_AddRemoveHas_RoundTripsThroughStore()
        {
            int id = TagRegistry.GetId("test.high.tag.280");
            That(id, Is.GreaterThanOrEqualTo(256), "前置：第 281 个注册名落在高位段");

            Entity entity = CreateTagEntity();
            That(_tagOps.AddTag(_world, entity, id), Is.True);
            That(TagOps.HasTagRouted(_world, entity, id), Is.True);
            That(_world.Get<GameplayTagContainer>(entity).HasTag(200), Is.False, "高位不污染内嵌镜像");

            That(_tagOps.RemoveTag(_world, entity, id), Is.True);
            That(TagOps.HasTagRouted(_world, entity, id), Is.False);
            That(_tagOps.RemoveTag(_world, entity, id), Is.False, "幂等：已不在则不改");
        }

        [Test]
        public void HighTag_Counts_TrackOnSparseContainer()
        {
            int id = TagRegistry.GetId("test.high.tag.290");
            Entity entity = CreateTagEntity();
            _tagOps.AddTag(_world, entity, id);
            _tagOps.AddTag(_world, entity, id);

            ref TagCountContainer counts = ref _world.Get<TagCountContainer>(entity);
            bool found = false;
            for (int i = 0; i < counts.Count; i++)
            {
                if (counts.TagIds[i] == id)
                {
                    found = true;
                    That(counts.Counts[i], Is.EqualTo((ushort)2), "稀疏计数容器天然支持任意 id");
                }
            }

            That(found, Is.True);
        }

        [Test]
        public void HighTag_DeferredTrigger_EnqueuesTagChanged()
        {
            int id = TagRegistry.GetId("test.high.tag.285");
            Entity entity = CreateTagEntity();
            _tagOps.AddTag(_world, entity, id);

            var triggerQueue = new DeferredTriggerQueue(64);
            var collection = new DeferredTriggerCollectionSystem(_world, triggerQueue, _tagOps, _tagOps.DirtyEntities);
            collection.Update(0f);
            That(triggerQueue.TagTriggerCount, Is.EqualTo(1), "首挂高标签触发 absent→present");
            That(triggerQueue.GetTagTrigger(0).TagId, Is.EqualTo(id));
            triggerQueue.Clear();

            _tagOps.RemoveTag(_world, entity, id);
            collection.Update(0f);
            That(triggerQueue.TagTriggerCount, Is.EqualTo(1), "摘除触发 present→absent");
            That(triggerQueue.GetTagTrigger(0).IsPresent, Is.False);
        }

        [Test]
        public void LowTag_MirrorToStore_StaysConsistentWithEmbedded()
        {
            Entity entity = CreateTagEntity();
            _tagOps.AddTag(_world, entity, 300); // 高位建行
            _tagOps.AddTag(_world, entity, 42);  // 低位 + 镜像

            var store = WorldAttributeStoreAmbient.Current!;
            store.TryGetRow(entity, out int row);
            That(store.HasTag(row, 42), Is.True);
            That(store.HasTag(row, 300), Is.True);
        }

        [Test]
        public void HighTag_RuleDeclaration_FailsClosedAtRegistration()
        {
            int id = TagRegistry.GetId("test.high.tag.270");
            Throws<ArgumentOutOfRangeException>(() => _tagOps.RegisterTagRuleSet(id, new TagRuleSet()),
                "规则引擎按 256 编译——高 id 规则在注册期即被拒绝，不静默降级（P3 对齐面）");
        }

        [Test]
        public void HighTag_WithoutStore_FailsClosed()
        {
            WorldAttributeStoreAmbient.Reset();
            int id = TagRegistry.GetId("test.high.tag.280");
            Entity entity = CreateTagEntity();
            InvalidOperationException ex = Throws<InvalidOperationException>(() =>
                _tagOps.AddTag(_world, entity, id))!;
            That(ex.Message, Does.Contain("HighLaneUnavailable"));
        }
    }
}
