using System;
using System.Diagnostics;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using NUnit.Framework;

namespace Ludots.Tests.GAS;

[TestFixture]
public sealed class EffectTransactionIndexTests
{
    [TestCase(1000)]
    [TestCase(5000)]
    [TestCase(10000)]
    public void GameplayState_DistinctEntities_CommitAndReuseWithinFrameBudget(int count)
    {
        using var world = World.Create();
        var effects = new Entity[count];
        for (int i = 0; i < count; i++) effects[i] = world.Create(new GameplayEffect());
        using var transaction = new EffectPhaseSideEffectTransaction(world, null, null, null, null, count);
        for (int i = 0; i < 16; i++) RunStatePass(transaction, effects, i);
        var samples = new double[9];
        long allocated = 0;
        for (int i = 0; i < samples.Length; i++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            long started = Stopwatch.GetTimestamp();
            RunStatePass(transaction, effects, 100 + i);
            samples[i] = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            allocated += GC.GetAllocatedBytesForCurrentThread() - before;
        }
        Array.Sort(samples);
        TestContext.Out.WriteLine($"state-transaction count={count} medianMs={samples[4]:F3} p95Ms={samples[8]:F3} allocated={allocated}");
        Assert.That(allocated, Is.Zero);
        Assert.That(samples[4], Is.LessThan(1000d / 60d));
        for (int i = 0; i < count; i++)
            Assert.That(world.Get<GameplayEffect>(effects[i]).RemainingTicks, Is.EqualTo(108 + i));
    }

    private static void RunStatePass(EffectPhaseSideEffectTransaction transaction, Entity[] effects, int tick)
    {
        transaction.Begin();
        for (int i = 0; i < effects.Length; i++)
        {
            var effect = new GameplayEffect { RemainingTicks = tick + i };
            transaction.StageGameplayEffectState(effects[i], in effect);
        }
        for (int i = effects.Length - 1; i >= 0; i--)
        {
            if (!transaction.TryGetGameplayEffectState(effects[i], out var effect) || effect.RemainingTicks != tick + i)
                throw new InvalidOperationException("Staged state was not retained for the correct entity.");
            transaction.StageGameplayEffectState(effects[i], in effect);
        }
        transaction.Commit();
    }

    [Test]
    public void StateCapacity_RestagingRollbackAndEntityVersionReuseRemainIsolated()
    {
        using var world = World.Create();
        Entity old = world.Create(new GameplayEffect());
        Entity other = world.Create(new GameplayEffect());
        using var transaction = new EffectPhaseSideEffectTransaction(world, null, null, null, null, 1);
        var value = new GameplayEffect { RemainingTicks = 7 };
        transaction.Begin();
        transaction.StageGameplayEffectState(old, in value);
        transaction.StageGameplayEffectState(old, in value);
        var error = Assert.Throws<InvalidOperationException>(() => transaction.StageGameplayEffectState(other, in value));
        Assert.That(error!.Message, Does.StartWith(EffectPhaseSideEffectTransaction.CapacityExceededError));
        transaction.Rollback();
        Assert.That(world.Get<GameplayEffect>(old).RemainingTicks, Is.Zero);

        transaction.Begin();
        transaction.StageGameplayEffectState(old, in value);
        world.Destroy(old);
        Entity replacement = world.Create(new GameplayEffect());
        Assert.That(replacement.Id, Is.EqualTo(old.Id));
        Assert.That(replacement.Version, Is.Not.EqualTo(old.Version));
        Assert.That(transaction.TryGetGameplayEffectState(replacement, out _), Is.False);
        Assert.Throws<InvalidOperationException>(() => transaction.Commit());
        transaction.Rollback();
        transaction.Begin();
        transaction.StageGameplayEffectState(replacement, in value);
        transaction.Commit();
        Assert.That(world.Get<GameplayEffect>(replacement).RemainingTicks, Is.EqualTo(7));
        Assert.That(transaction.TryGetGameplayEffectState(old, out _), Is.False);
    }

    [Test]
    public void BlackboardChannels_AtCapacityReadOwnWritesRollbackAndReuse()
    {
        using var world = World.Create();
        var targets = new Entity[3];
        for (int i = 0; i < targets.Length; i++)
            targets[i] = world.Create(new BlackboardFloatBuffer(), new BlackboardIntBuffer(), new BlackboardEntityBuffer());
        using var transaction = new EffectPhaseSideEffectTransaction(world, null, null, null, null, 2);
        for (int pass = 0; pass < 2; pass++)
        {
            transaction.Begin();
            for (int i = 0; i < 2; i++)
            {
                transaction.StageBlackboardFloat(targets[i], 1, i + 1);
                transaction.StageBlackboardInt(targets[i], 1, i + 2);
                transaction.StageBlackboardEntity(targets[i], 1, targets[2]);
            }
            for (int i = 1; i >= 0; i--)
            {
                transaction.StageBlackboardFloat(targets[i], 1, i + 10);
                transaction.StageBlackboardInt(targets[i], 1, i + 20);
                transaction.StageBlackboardEntity(targets[i], 1, targets[1 - i]);
                Assert.That(transaction.TryReadBlackboardFloat(targets[i], 1, out float f), Is.True);
                Assert.That(f, Is.EqualTo(i + 10));
                Assert.That(transaction.TryReadBlackboardInt(targets[i], 1, out int n), Is.True);
                Assert.That(n, Is.EqualTo(i + 20));
                Assert.That(transaction.TryReadBlackboardEntity(targets[i], 1, out Entity e), Is.True);
                Assert.That(e, Is.EqualTo(targets[1 - i]));
            }
            Assert.Throws<InvalidOperationException>(() => transaction.StageBlackboardFloat(targets[2], 1, 1));
            Assert.Throws<InvalidOperationException>(() => transaction.StageBlackboardInt(targets[2], 1, 1));
            Assert.Throws<InvalidOperationException>(() => transaction.StageBlackboardEntity(targets[2], 1, targets[0]));
            if (pass == 0) transaction.Rollback();
            else transaction.Commit();
            for (int i = 0; i < 2; i++)
            {
                Assert.That(world.Get<BlackboardFloatBuffer>(targets[i]).TryGet(1, out float f), Is.EqualTo(pass == 1));
                Assert.That(f, Is.EqualTo(pass == 1 ? i + 10 : 0));
                Assert.That(world.Get<BlackboardIntBuffer>(targets[i]).TryGet(1, out int n), Is.EqualTo(pass == 1));
                Assert.That(n, Is.EqualTo(pass == 1 ? i + 20 : 0));
                Assert.That(world.Get<BlackboardEntityBuffer>(targets[i]).TryGet(1, out Entity e), Is.EqualTo(pass == 1));
                if (pass == 1) Assert.That(e, Is.EqualTo(targets[1 - i]));
            }
        }
    }

    [Test]
    public void ListenerRegistration_DistinctSourceAndTargetCountsRemainLinearAndReusable()
    {
        const int count = 10000;
        using var world = World.Create();
        var contexts = new EffectContext[count];
        for (int i = 0; i < count; i++)
            contexts[i] = new EffectContext
            {
                Source = world.Create(new EffectPhaseListenerBuffer()),
                Target = world.Create(new EffectPhaseListenerBuffer())
            };
        var setup = new EffectPhaseListenerBuffer();
        Assert.That(setup.TryAdd(0, 0, EffectPhaseId.OnApply, PhaseListenerScope.Source,
            PhaseListenerActionFlags.PublishEvent, 0, 1, 0, 1), Is.True);
        Assert.That(setup.TryAdd(0, 0, EffectPhaseId.OnApply, PhaseListenerScope.Target,
            PhaseListenerActionFlags.PublishEvent, 0, 2, 0, 1), Is.True);
        using var transaction = new EffectPhaseSideEffectTransaction(world, null, null, null, null, count);
        var samples = new double[9];
        long allocated = 0;
        for (int pass = 0; pass < 25; pass++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            long started = Stopwatch.GetTimestamp();
            transaction.Begin();
            for (int i = 0; i < count; i++) transaction.StageListenerRegistration(in contexts[i], in setup, i + 1);
            transaction.Commit();
            if (pass >= 16)
            {
                samples[pass - 16] = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                allocated += GC.GetAllocatedBytesForCurrentThread() - before;
            }
            for (int i = 0; i < count; i++)
            {
                Assert.That(world.Get<EffectPhaseListenerBuffer>(contexts[i].Source).Count, Is.EqualTo(1));
                Assert.That(world.Get<EffectPhaseListenerBuffer>(contexts[i].Target).Count, Is.EqualTo(1));
                world.Get<EffectPhaseListenerBuffer>(contexts[i].Source) = default;
                world.Get<EffectPhaseListenerBuffer>(contexts[i].Target) = default;
            }
        }
        Array.Sort(samples);
        TestContext.Out.WriteLine($"listener-registration count={count} distinctEntities={count * 2} medianMs={samples[4]:F3} p95Ms={samples[8]:F3} allocated={allocated}");
        Assert.That(allocated, Is.Zero);
        Assert.That(samples[4], Is.LessThan(1000d / 60d));
    }

    [Test]
    public void ListenerRemoval_DeduplicatesEntityAndOwnerAndClearsOnRollback()
    {
        using var world = World.Create();
        Entity target = world.Create(new EffectPhaseListenerBuffer());
        Entity source = world.Create(new EffectPhaseListenerBuffer());
        foreach (Entity entity in new[] { target, source })
        {
            ref var listeners = ref world.Get<EffectPhaseListenerBuffer>(entity);
            Assert.That(listeners.TryAdd(0, 0, EffectPhaseId.OnApply, PhaseListenerScope.Target,
                PhaseListenerActionFlags.PublishEvent, 0, 1, 0, 1), Is.True);
            Assert.That(listeners.TryAdd(0, 0, EffectPhaseId.OnApply, PhaseListenerScope.Target,
                PhaseListenerActionFlags.PublishEvent, 0, 1, 0, 2), Is.True);
        }
        using var transaction = new EffectPhaseSideEffectTransaction(world, null, null, null, null, 2);
        var context = new EffectContext { Target = target, Source = source };
        for (int pass = 0; pass < 2; pass++)
        {
            transaction.Begin();
            for (int repeat = 0; repeat < 4; repeat++)
            {
                transaction.StageListenerRemoval(in context, 1);
                transaction.StageListenerRemoval(in context, 2);
            }
            if (pass == 0) transaction.Rollback();
            else transaction.Commit();
            Assert.That(world.Get<EffectPhaseListenerBuffer>(target).Count, Is.EqualTo(pass == 0 ? 2 : 0));
            Assert.That(world.Get<EffectPhaseListenerBuffer>(source).Count, Is.EqualTo(pass == 0 ? 2 : 0));
        }
    }
}
