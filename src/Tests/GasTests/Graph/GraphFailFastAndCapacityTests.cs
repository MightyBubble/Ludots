using System;
using Arch.Core;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Map.Hex;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Core.Mathematics;
using Ludots.Core.Spatial;
using Ludots.Core.Registry;
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
            Throws<InvalidOperationException>(() => api.QueryRadius(new IntVector2(0, 0), radiusCm: 1f, buffer: Span<Entity>.Empty));
        }

        [Test]
        public void GasGraphRuntimeApi_QueryRadius_UsesTargetPositionAsWorldCentimeters()
        {
            using var world = World.Create();
            var spatial = new RecordingSpatialQueries();
            var api = new GasGraphRuntimeApi(world, spatialQueries: spatial);

            api.QueryRadius(new IntVector2(1234, -567), radiusCm: 89.6f, buffer: Span<Entity>.Empty);

            That(spatial.LastRadiusCenter, Is.EqualTo(new WorldCmInt2(1234, -567)));
            That(spatial.LastRadiusCm, Is.EqualTo(90));
        }

        [Test]
        public void GasGraphRuntimeApi_QueryHexRange_ConvertsFromWorldCentimetersAtHexBoundary()
        {
            using var world = World.Create();
            var spatial = new RecordingSpatialQueries();
            var coordinates = new SpatialCoordinateConverter();
            var api = new GasGraphRuntimeApi(world, spatialQueries: spatial, coords: coordinates);
            var targetPosCm = new IntVector2(1234, -567);

            api.QueryHexRange(targetPosCm, hexRadius: 2, buffer: Span<Entity>.Empty);

            HexCoordinates expected = coordinates.WorldToHex(new WorldCmInt2(targetPosCm.X, targetPosCm.Y));
            That(spatial.LastHexCenter, Is.EqualTo(expected));
            That(spatial.LastHexRadius, Is.EqualTo(2));
        }

        [Test]
        public void GameplayEventBus_WhenCapacityExceeded_ThrowsBeforeDropping()
        {
            var bus = new GameplayEventBus();
            for (int i = 0; i < GasConstants.MAX_GAMEPLAY_EVENTS_PER_FRAME; i++)
            {
                bus.Publish(new GameplayEvent { TagId = i });
            }

            var error = Throws<InvalidOperationException>(() =>
                bus.Publish(new GameplayEvent { TagId = GasConstants.MAX_GAMEPLAY_EVENTS_PER_FRAME + 1 }));

            That(error!.Message, Does.StartWith(GameplayEventBus.CapacityExceededError));
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

            q.ConsumePrefix(32);

            That(q.Count, Is.EqualTo(GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME - 32 + 9));
            That(q.OverflowCount, Is.EqualTo(0));
        }

        [Test]
        public void EffectRequestQueue_WhenTotalCapacityExceeded_ThrowsBeforeDropping()
        {
            var q = new EffectRequestQueue();
            for (int i = 0; i < q.TotalCapacity; i++)
            {
                q.Publish(new EffectRequest { TemplateId = i + 1 });
            }

            var error = Throws<InvalidOperationException>(() =>
                q.Publish(new EffectRequest { TemplateId = q.TotalCapacity + 1 }));

            That(error!.Message, Does.StartWith(EffectRequestQueue.CapacityExceededError));
            That(q.Count, Is.EqualTo(q.Capacity));
            That(q.OverflowCount, Is.EqualTo(q.Capacity));
        }

        [Test]
        public void GraphOutputValueStore_WhenConfiguredCapacityIsExceeded_FailsWithoutResizingOrMutation()
        {
            using var world = World.Create();
            var keys = new StringIntRegistry(
                capacity: 8,
                startId: 1,
                invalidId: 0,
                comparer: StringComparer.Ordinal);
            int keyId = keys.Register("graph.output.test");
            var values = new GraphOutputValueStore(keys, initialCapacity: 2);
            Entity first = world.Create();
            Entity second = world.Create();
            Entity rejected = world.Create();

            GraphOutputValueHandle firstHandle = values.SetInt(first, keyId, 10);
            GraphOutputValueHandle secondHandle = values.SetInt(second, keyId, 20);

            var error = Assert.Throws<InvalidOperationException>(() => values.SetInt(rejected, keyId, 30));

            Assert.That(error!.Message, Does.StartWith("GAS.GRAPH_OUTPUT.ERR.CapacityExceeded"));
            Assert.That(values.ActiveCount, Is.EqualTo(2));
            Assert.That(values.TryGetView(firstHandle, out GraphOutputValueView firstView), Is.True);
            Assert.That(firstView.IntValue, Is.EqualTo(10));
            Assert.That(values.TryGetView(secondHandle, out GraphOutputValueView secondView), Is.True);
            Assert.That(secondView.IntValue, Is.EqualTo(20));
            Assert.That(values.TryGet(rejected, "graph.output.test", out _), Is.False);
        }

        [Test]
        public void GasGraphRuntimeApi_WriteBlackboardFloat_WithoutBuffer_Throws()
        {
            using var world = World.Create();
            var entity = world.Create();
            var api = new GasGraphRuntimeApi(world);

            var error = Throws<InvalidOperationException>(() => api.WriteBlackboardFloat(entity, 7, 1f));

            That(error!.Message, Does.StartWith(GasGraphRuntimeApi.MissingBlackboardError));
            That(error.Message, Does.Contain(nameof(BlackboardFloatBuffer)));
            That(world.Has<BlackboardFloatBuffer>(entity), Is.False);
        }

        [Test]
        public void GasGraphRuntimeApi_WriteBlackboardInt_WithoutBuffer_Throws()
        {
            using var world = World.Create();
            var entity = world.Create();
            var api = new GasGraphRuntimeApi(world);

            var error = Throws<InvalidOperationException>(() => api.WriteBlackboardInt(entity, 7, 1));

            That(error!.Message, Does.StartWith(GasGraphRuntimeApi.MissingBlackboardError));
            That(error.Message, Does.Contain(nameof(BlackboardIntBuffer)));
            That(world.Has<BlackboardIntBuffer>(entity), Is.False);
        }

        [Test]
        public void GasGraphRuntimeApi_WriteBlackboardEntity_WithoutBuffer_Throws()
        {
            using var world = World.Create();
            var entity = world.Create();
            var value = world.Create();
            var api = new GasGraphRuntimeApi(world);

            var error = Throws<InvalidOperationException>(() => api.WriteBlackboardEntity(entity, 7, value));

            That(error!.Message, Does.StartWith(GasGraphRuntimeApi.MissingBlackboardError));
            That(error.Message, Does.Contain(nameof(BlackboardEntityBuffer)));
            That(world.Has<BlackboardEntityBuffer>(entity), Is.False);
        }

        [Test]
        public void GasGraphRuntimeApi_WriteBlackboardFloat_DeadEntity_Throws()
        {
            using var world = World.Create();
            var entity = world.Create(new BlackboardFloatBuffer());
            world.Destroy(entity);
            var api = new GasGraphRuntimeApi(world);

            var error = Throws<InvalidOperationException>(() => api.WriteBlackboardFloat(entity, 7, 1f));

            That(error!.Message, Does.StartWith(GasGraphRuntimeApi.MissingBlackboardError));
            That(error.Message, Does.Contain(nameof(BlackboardFloatBuffer)));
        }

        private sealed class RecordingSpatialQueries : ISpatialQueryService
        {
            public WorldCmInt2 LastRadiusCenter { get; private set; }
            public int LastRadiusCm { get; private set; }
            public HexCoordinates LastHexCenter { get; private set; }
            public int LastHexRadius { get; private set; }

            public SpatialQueryResult QueryAabb(in WorldAabbCm bounds, Span<Entity> buffer) => default;

            public SpatialQueryResult QueryRadius(WorldCmInt2 center, int radiusCm, Span<Entity> buffer)
            {
                LastRadiusCenter = center;
                LastRadiusCm = radiusCm;
                return default;
            }

            public SpatialQueryResult QueryCone(WorldCmInt2 origin, int directionDeg, int halfAngleDeg, int rangeCm, Span<Entity> buffer) => default;
            public SpatialQueryResult QueryRectangle(WorldCmInt2 center, int halfWidthCm, int halfHeightCm, int rotationDeg, Span<Entity> buffer) => default;
            public SpatialQueryResult QueryLine(WorldCmInt2 origin, int directionDeg, int lengthCm, int halfWidthCm, Span<Entity> buffer) => default;
            public SpatialQueryResult QueryHexRange(HexCoordinates center, int hexRadius, Span<Entity> buffer)
            {
                LastHexCenter = center;
                LastHexRadius = hexRadius;
                return default;
            }
            public SpatialQueryResult QueryHexRing(HexCoordinates center, int hexRadius, Span<Entity> buffer) => default;
        }
    }
}
