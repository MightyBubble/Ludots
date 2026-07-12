using System;
using Arch.Core;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Map.Hex;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Core.Mathematics;
using Ludots.Core.Spatial;
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
