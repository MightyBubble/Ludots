using System;
using Arch.Core;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Core.Mathematics;
using NUnit.Framework;
using static NUnit.Framework.Assert;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    public class GraphFailFastAndCapacityTests
    {
        [Test]
        public void GasGraphRuntimeApi_ApplyEffectTemplate_WithoutRequestQueue_Throws()
        {
            using var world = World.Create();
            var api = new GasGraphRuntimeApi(world, spatialQueries: null, coords: null, eventBus: null, effectRequests: null);
            Throws<InvalidOperationException>(() => api.ApplyEffectTemplate(default, default, templateId: 1));
        }

        [Test]
        public void GasGraphRuntimeApi_SendEvent_WithoutEventBus_Throws()
        {
            using var world = World.Create();
            var api = new GasGraphRuntimeApi(world, spatialQueries: null, coords: null, eventBus: null, effectRequests: new EffectRequestQueue());
            Throws<InvalidOperationException>(() => api.SendEvent(default, default, eventTagId: 1, magnitude: 1f));
        }

        [Test]
        public void GasGraphRuntimeApi_QueryRadius_WithoutSpatialService_Throws()
        {
            using var world = World.Create();
            var api = new GasGraphRuntimeApi(world, spatialQueries: null, coords: null, eventBus: null, effectRequests: new EffectRequestQueue());
            Throws<InvalidOperationException>(() => api.QueryRadius(new IntVector2(0, 0), radius: 1f, buffer: Span<Entity>.Empty));
        }

        [Test]
        public void GameplayEventBus_DropsAfterCapacity_AndReportsDropped()
        {
            var bus = new GameplayEventBus();
            for (int i = 0; i < GasConstants.MAX_GAMEPLAY_EVENTS_PER_FRAME + 7; i++)
            {
                bus.Publish(new GameplayEvent { TagId = i });
            }
            bus.Update();
            That(bus.Events.Count, Is.EqualTo(GasConstants.MAX_GAMEPLAY_EVENTS_PER_FRAME));
            That(bus.DroppedEventsLastUpdate, Is.EqualTo(7));
        }

        [Test]
        public void EffectRequestQueue_OverflowsAndRefillsOnConsume()
        {
            var q = new EffectRequestQueue();
            for (int i = 0; i < GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME + 9; i++)
            {
                q.Publish(new EffectRequest { TemplateId = i + 1 });
            }

            That(q.Count, Is.EqualTo(GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME));
            That(q.OverflowCount, Is.EqualTo(9));
            That(q.DroppedCount, Is.EqualTo(0));

            q.ConsumePrefix(32);

            // After consuming 32 from main (4096→4064), overflow 9 refilled → 4073 total.
            That(q.Count, Is.EqualTo(GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME - 32 + 9));
            That(q.OverflowCount, Is.EqualTo(0));
            That(q.DroppedCount, Is.EqualTo(0));
        }
    }
}

