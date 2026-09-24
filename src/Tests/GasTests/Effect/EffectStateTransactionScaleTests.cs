using System.Diagnostics;
using Arch.Core;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using NUnit.Framework;

namespace Ludots.Tests.GAS;

[TestFixture]
public sealed class EffectStateTransactionScaleTests
{
    [Test]
    public void TenThousandEffects_StageReadCommitAndRollbackWithoutAllocating()
    {
        const int count = 10000;
        using World world = World.Create();
        var entities = new Entity[count];
        for (int i = 0; i < count; i++)
            entities[i] = world.Create(new GameplayEffect { RemainingTicks = 100 });
        using var transaction = new EffectPhaseSideEffectTransaction(world, null, null, null, null, count);
        StageAll(transaction, entities, 90);
        transaction.Rollback();

        long before = GC.GetAllocatedBytesForCurrentThread();
        long start = Stopwatch.GetTimestamp();
        StageAll(transaction, entities, 80);
        transaction.Commit();
        double elapsedMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        for (int i = 0; i < count; i++)
            Assert.That(world.Get<GameplayEffect>(entities[i]).RemainingTicks, Is.EqualTo(80));
        Assert.That(transaction.TryGetGameplayEffectState(entities[0], out _), Is.False);
        Assert.That(allocated, Is.Zero);
        TestContext.Out.WriteLine($"effects={count}, stage-read-commit-ms={elapsedMs:F3}, allocated={allocated}");

        StageAll(transaction, entities, 60);
        transaction.Rollback();
        Assert.That(transaction.TryGetGameplayEffectState(entities[0], out _), Is.False);
        for (int i = 0; i < count; i++)
            Assert.That(world.Get<GameplayEffect>(entities[i]).RemainingTicks, Is.EqualTo(80));
    }

    [Test]
    public void FullCapacity_AllowsRestagingAndRejectsAnotherEntity()
    {
        using World world = World.Create();
        Entity first = world.Create(new GameplayEffect { RemainingTicks = 100 });
        Entity second = world.Create(new GameplayEffect { RemainingTicks = 200 });
        using var transaction = new EffectPhaseSideEffectTransaction(world, null, null, null, null, 1);
        transaction.Begin();
        var effect = new GameplayEffect { RemainingTicks = 90 };
        transaction.StageGameplayEffectState(first, in effect);
        effect.RemainingTicks = 80;
        transaction.StageGameplayEffectState(first, in effect);
        Assert.That(transaction.TryGetGameplayEffectState(first, out var staged), Is.True);
        Assert.That(staged.RemainingTicks, Is.EqualTo(80));
        Assert.That(world.Get<GameplayEffect>(first).RemainingTicks, Is.EqualTo(100));
        Assert.That(() => transaction.StageGameplayEffectState(second, in effect),
            Throws.InvalidOperationException.With.Message.StartsWith(EffectPhaseSideEffectTransaction.CapacityExceededError));
        transaction.Rollback();
        transaction.Begin();
        transaction.StageGameplayEffectState(second, in effect);
        transaction.Commit();
        Assert.That(world.Get<GameplayEffect>(first).RemainingTicks, Is.EqualTo(100));
        Assert.That(world.Get<GameplayEffect>(second).RemainingTicks, Is.EqualTo(80));
    }

    private static void StageAll(EffectPhaseSideEffectTransaction transaction, Entity[] entities, int ticks)
    {
        transaction.Begin();
        var state = new GameplayEffect { RemainingTicks = ticks };
        foreach (Entity entity in entities)
        {
            transaction.StageGameplayEffectState(entity, in state);
            if (!transaction.TryGetGameplayEffectState(entity, out var staged) || staged.RemainingTicks != ticks)
                throw new InvalidOperationException("Staged effect state was not readable.");
        }
    }
}
