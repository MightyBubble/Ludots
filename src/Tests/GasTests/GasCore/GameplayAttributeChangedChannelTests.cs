using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Gameplay;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Presentation;
using Ludots.Core.Gameplay.GAS.Systems;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Presentation.Events;
using Ludots.Core.Presentation.Systems;
using Ludots.Platform.Abstractions;
using NUnit.Framework;
using static NUnit.Framework.Assert;

namespace Ludots.Tests.GAS
{
    /// <summary>
    /// GameplayAttributeChangedChannel 的合同守卫：
    /// - 版本语义：仅可见状态变化递增，幂等 Mark / 空清除 / 摘除缺席条目不递增；
    /// - 零变更 tick 零结构操作：属性变更广播路径（变异写方 → 投影 → 清理）
    ///   不触发任何原型搬迁——实体的 Archetype 引用全程不变；
    /// - 版本门控：投影对无变化窗口零遍历、同窗口不重复发射事件。
    /// </summary>
    [TestFixture]
    public class GameplayAttributeChangedChannelTests
    {
        [Test]
        public void Version_IncreasesOnlyOnVisibleStateChange()
        {
            using var world = World.Create();
            var channel = new GameplayAttributeChangedChannel();
            Entity entity = world.Create();
            Entity stranger = world.Create();

            That(channel.Version, Is.EqualTo(0));
            That(channel.Mark(entity, 3), Is.True);
            That(channel.Version, Is.EqualTo(1));
            That(channel.Count, Is.EqualTo(1));

            That(channel.Mark(entity, 3), Is.False, "重复 Mark 同一位不得递增版本。");
            That(channel.Version, Is.EqualTo(1));

            That(channel.Mark(entity, 5), Is.True);
            That(channel.Version, Is.EqualTo(2));
            That(channel.TryGetBits(entity, out ulong bits), Is.True);
            That(bits, Is.EqualTo((1UL << 3) | (1UL << 5)));

            That(channel.Mark(entity, 999), Is.False, "越界属性 id 静默忽略。");
            That(channel.Version, Is.EqualTo(2));

            channel.SetBits(entity, bits);
            That(channel.Version, Is.EqualTo(2), "回写同值不得递增版本。");
            channel.SetBits(entity, 1UL << 3);
            That(channel.Version, Is.EqualTo(3));

            That(channel.Remove(stranger), Is.False);
            That(channel.Version, Is.EqualTo(3), "摘除缺席条目不得递增版本。");
            That(channel.Remove(entity), Is.True);
            That(channel.Version, Is.EqualTo(4));
            That(channel.Count, Is.EqualTo(0));

            channel.Clear();
            That(channel.Version, Is.EqualTo(4), "清除空通道不得递增版本。");

            channel.MarkMask(entity, 1UL << 2);
            That(channel.Version, Is.EqualTo(5));
            channel.Clear();
            That(channel.Version, Is.EqualTo(6), "清除非空通道必须递增版本（消费方门控依赖它）。");
            That(channel.Count, Is.EqualTo(0));
        }

        [Test]
        public void AttributeChangeBroadcast_FullTick_PerformsZeroStructuralOperations()
        {
            using var world = World.Create();
            int attributeId = AttributeRegistry.Register("tests.attribute-channel.broadcast.zero-structural");
            var aggregateDirty = new AttributeAggregateDirtyRegistry();
            var tagOps = new TagOps(
                new DirtyEntityQueue(GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME),
                new TagRuleRegistry(),
                aggregateDirty: aggregateDirty);
            var gasEvents = new GasPresentationEventBuffer(8);
            var stream = new PresentationEventStream(64);
            var ownerChanges = new PresentationOwnerChangeBuffer(16);
            using var aggregator = new AttributeAggregatorSystem(world, tagOps: tagOps, aggregateDirty: aggregateDirty);
            using var projection = new GameplayPresentationProjectionSystem(
                world,
                new GameplayEventBus(),
                stream,
                new GameSession(),
                gasEvents,
                ownerChanges,
                tagOps.AttributeChanges,
                enabled: true);
            using var clear = new ClearPresentationFlagsSystem(world, tagOps.AttributeChanges);

            Entity first = world.Create(new AttributeBuffer(), new DirtyFlags(), new ActiveEffectContainer());
            Entity second = world.Create(new AttributeBuffer(), new DirtyFlags(), new ActiveEffectContainer());
            world.Get<AttributeBuffer>(first).SetBase(attributeId, 100f);
            world.Get<AttributeBuffer>(second).SetBase(attributeId, 50f);

            var archetypesBefore = new Dictionary<Entity, Archetype>
            {
                [first] = world.GetArchetype(first),
                [second] = world.GetArchetype(second),
            };
            int archetypesCountBefore = world.Archetypes.Count;

            AttributeMutationOps.SetCurrent(world, first, attributeId, 90f, tagOps);
            AttributeMutationOps.SetCurrent(world, second, attributeId, 40f, tagOps);

            aggregator.Update(0f);
            projection.Update(1f / 60f);
            clear.Update(0f);

            That(tagOps.AttributeChanges.Count, Is.EqualTo(0), "tick 尾清理后通道必须为空。");
            That(stream.Count, Is.EqualTo(2), "两个属性变更必须各发射一次 AttributeValueChanged。");
            int emitted = 0;
            foreach (ref readonly PresentationEvent evt in stream.GetSpan())
            {
                That(evt.Kind, Is.EqualTo(PresentationEventKind.AttributeValueChanged));
                That(evt.Source, Is.EqualTo(evt.Target));
                emitted++;
            }
            That(emitted, Is.EqualTo(2));
            That(ownerChanges.Count, Is.EqualTo(2));

            That(world.GetArchetype(first), Is.SameAs(archetypesBefore[first]),
                "属性变更广播路径不得触发任何原型搬迁。");
            That(world.GetArchetype(second), Is.SameAs(archetypesBefore[second]),
                "属性变更广播路径不得触发任何原型搬迁。");
            That(world.Archetypes.Count, Is.EqualTo(archetypesCountBefore));
        }

        [Test]
        public void ProjectionVersionGate_SkipsUnchangedWindows_AndEmitsOncePerMark()
        {
            using var world = World.Create();
            int attributeId = AttributeRegistry.Register("tests.attribute-channel.projection.version-gate");
            var aggregateDirty = new AttributeAggregateDirtyRegistry();
            var tagOps = new TagOps(
                new DirtyEntityQueue(GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME),
                new TagRuleRegistry(),
                aggregateDirty: aggregateDirty);
            var stream = new PresentationEventStream(64);
            var ownerChanges = new PresentationOwnerChangeBuffer(16);
            using var projection = new GameplayPresentationProjectionSystem(
                world,
                new GameplayEventBus(),
                stream,
                new GameSession(),
                new GasPresentationEventBuffer(8),
                ownerChanges,
                tagOps.AttributeChanges,
                enabled: true);
            using var clear = new ClearPresentationFlagsSystem(world, tagOps.AttributeChanges);

            Entity entity = world.Create(new AttributeBuffer(), new DirtyFlags(), new ActiveEffectContainer());
            world.Get<AttributeBuffer>(entity).SetBase(attributeId, 100f);

            projection.Update(1f / 60f);
            That(stream.Count, Is.EqualTo(0), "空通道首帧不得发射事件。");

            int versionAfterFirstIdleWindow = tagOps.AttributeChanges.Version;
            projection.Update(1f / 60f);
            That(stream.Count, Is.EqualTo(0), "版本未变的窗口必须整段跳过。");
            That(tagOps.AttributeChanges.Version, Is.EqualTo(versionAfterFirstIdleWindow));

            AttributeMutationOps.SetCurrent(world, entity, attributeId, 75f, tagOps);
            projection.Update(1f / 60f);
            That(stream.Count, Is.EqualTo(1));
            That(stream.GetSpan()[0].KeyId, Is.EqualTo(attributeId));
            That(stream.GetSpan()[0].Magnitude, Is.EqualTo(75f));

            projection.Update(1f / 60f);
            That(stream.Count, Is.EqualTo(1), "同一窗口的残留标记不得重复发射事件。");

            clear.Update(0f);
            stream.Clear();
            ownerChanges.Clear();
            projection.Update(1f / 60f);
            That(stream.Count, Is.EqualTo(0), "清除后窗口必须零发射。");

            projection.Update(1f / 60f);
            That(stream.Count, Is.EqualTo(0), "零变更 tick 必须保持零结构操作与零发射。");
            That(world.GetArchetype(entity), Is.Not.Null);
        }
    }
}
