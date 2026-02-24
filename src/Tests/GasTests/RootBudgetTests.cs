using System;
using Arch.Core;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Systems;
using NUnit.Framework;
using static NUnit.Framework.Assert;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    public class RootBudgetTests
    {
        [Test]
        public void EffectRequestQueue_AssignsRootId_WhenMissing_AndPreservesExplicit()
        {
            var q = new EffectRequestQueue();

            q.Publish(new EffectRequest { RootId = 0, TemplateId = 1 });
            q.Publish(new EffectRequest { RootId = 0, TemplateId = 2 });
            q.Publish(new EffectRequest { RootId = 123, TemplateId = 3 });

            That(q.Count, Is.EqualTo(3));
            That(q[0].RootId, Is.Not.EqualTo(0));
            That(q[1].RootId, Is.Not.EqualTo(0));
            That(q[1].RootId, Is.Not.EqualTo(q[0].RootId));
            That(q[2].RootId, Is.EqualTo(123));
        }

        [Test]
        public void EffectRequestQueue_Reserve_ExpandsCapacity()
        {
            var q = new EffectRequestQueue(initialCapacity: 4096);
            int before = q.Capacity;
            q.Reserve(100_000);
            That(q.Capacity, Is.GreaterThanOrEqualTo(100_000));
            That(q.Capacity, Is.GreaterThan(before));
        }

        // Note: EffectCallbackComponent has been removed per the "Everything is Graph" architecture.
        // OnApply/OnExpire callbacks are now Phase Graph bindings in EffectPhaseGraphBindings.
        // Budget tests for Phase Graph-based callbacks will be added once graph programs
        // are available in the test fixture.

        [Test]
        public void EffectApplicationSystem_ProcessesInstantEffects_WithoutCallbacks()
        {
            var world = World.Create();
            try
            {
                var budget = new GasBudget();
                var requests = new EffectRequestQueue();
                var app = new EffectApplicationSystem(world, requests, budget);

                var source = world.Create();
                var target = world.Create();

                for (int i = 0; i < 10; i++)
                {
                    GameplayEffectFactory.CreateEffect(world, rootId: 1, source, target, durationTicks: 0, lifetimeKind: EffectLifetimeKind.Instant);
                }

                app.Update(0.016f);

                // Without callbacks, no EffectRequests should be published
                That(requests.Count, Is.EqualTo(0));
            }
            finally
            {
                world.Dispose();
            }
        }

        [Test]
        public void EffectDurationSystem_ExpiresEffects_WithoutCallbacks()
        {
            var world = World.Create();
            try
            {
                var budget = new GasBudget();
                var requests = new EffectRequestQueue();
                var clock = new DiscreteClock();
                var clocks = new GasClocks(clock);
                var conditions = new GasConditionRegistry();
                var lifetime = new EffectLifetimeSystem(world, clock, conditions, requests, budget);

                var source = world.Create();
                var target = world.Create();

                for (int i = 0; i < 10; i++)
                {
                    var e = GameplayEffectFactory.CreateEffect(world, rootId: 7, source, target, durationTicks: 0, lifetimeKind: EffectLifetimeKind.After);
                    ref var ge = ref world.Get<GameplayEffect>(e);
                    ge.State = EffectState.Committed;
                }

                lifetime.Update(0.016f);

                // Without callbacks, no EffectRequests should be published
                That(requests.Count, Is.EqualTo(0));
            }
            finally
            {
                world.Dispose();
            }
        }
    }
}
