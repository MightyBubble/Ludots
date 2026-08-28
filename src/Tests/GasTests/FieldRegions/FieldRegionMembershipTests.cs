using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.EntityCollections;
using Ludots.Core.Fields;
using Ludots.Core.Gameplay.FieldRegions;
using Ludots.Core.Map;
using Ludots.Core.Config;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Registry;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace Ludots.Tests.GAS.FieldRegions
{
    [TestFixture]
    public sealed class FieldRegionMembershipTests
    {
        private const string MapIdValue = "map_field_region_probe";

        private World _world = null!;
        private MapSessionManager _sessions = null!;
        private MapSession _session = null!;
        private FieldLayerRegistry _catalog = null!;
        private DiscreteIdFieldLayerData _layer = null!;
        private RegionEntityIndex _index = null!;
        private EntityCollectionStore _collections = null!;
        private TriggerManager _triggers = null!;
        private FieldRegionMembershipSystem _system = null!;
        private List<(EventKey Key, string Region, string LayerKey)> _events = null!;

        [SetUp]
        public void SetUp()
        {
            _world = World.Create();
            _catalog = new FieldLayerRegistry();
            _catalog.Register(
                "layerX", FieldLayerKind.DiscreteId, cellSizeCm: 100, chunkSizeCells: 8,
                FieldLayerDefaultValue.None, persistent: true, "test.writer", maxRegionIds: 16);
            _sessions = new MapSessionManager();
            _session = _sessions.CreateSession(new MapId(MapIdValue), new MapConfig());
            _session.Fields = FieldSessionStore.Create(_catalog, new[] { "layerX" });
            _layer = _session.Fields.Get<DiscreteIdFieldLayerData>(_catalog.GetId("layerX"));
            _layer.Regions.Register("r1");
            _layer.Regions.Register("r2");
            // cells (0..3, 0..3) = r1; cells (4..7, 0..3) = r2 (cellSize 100cm).
            for (int x = 0; x < 4; x++)
            {
                _layer.Field.Set(new FieldCell2D(x, 0), 1);
                _layer.Field.Set(new FieldCell2D(x + 4, 0), 2);
            }

            _index = FieldRegionMaterializer.Materialize(_world, _session);
            _session.RegionIndex = _index;
            _collections = new EntityCollectionStore(new StringIntRegistry());
            _triggers = new TriggerManager();
            _events = new List<(EventKey, string, string)>();
            _triggers.RegisterEventHandler(GameEvents.FieldRegionEntered, ctx => Capture(GameEvents.FieldRegionEntered, ctx));
            _triggers.RegisterEventHandler(GameEvents.FieldRegionExited, ctx => Capture(GameEvents.FieldRegionExited, ctx));
            _system = new FieldRegionMembershipSystem(
                _world, () => _sessions, _collections, _triggers, () => new ScriptContext());
        }

        [TearDown]
        public void TearDown()
        {
            _system.Dispose();
            _session.Dispose();
            _world.Dispose();
        }

        [Test]
        public void Materialize_CreatesOneEntityPerRegion_WithFootprintCounts()
        {
            Assert.That(_index.Count, Is.EqualTo(2));
            Assert.That(_index.TryResolve(_layer.LayerId, 1, out Entity r1), Is.True);
            Assert.That(_world.Get<RegionCm>(r1).RegionId, Is.EqualTo(1));
            Assert.That(_world.Get<RegionFootprintCm>(r1).CellCount, Is.EqualTo(4));
            Assert.That(_world.Get<MapEntity>(r1).MapId, Is.EqualTo(new MapId(MapIdValue)));
            Assert.That(_index.TryResolve(_layer.LayerId, 2, out Entity r2), Is.True);
            Assert.That(_world.Get<RegionFootprintCm>(r2).CellCount, Is.EqualTo(4));
        }

        [Test]
        public void Materialize_EmptyStore_YieldsEmptyIndex()
        {
            var emptySession = _sessions.CreateSession(new MapId("map_no_fields"), new MapConfig());
            Assert.That(FieldRegionMaterializer.Materialize(_world, emptySession).Count, Is.EqualTo(0));
        }

        [Test]
        public void FirstObservation_FiresEntered_AndProjectsRoster()
        {
            Entity unit = SpawnTracked(150, 50);   // cell (1,0) → r1

            _system.Update(1 / 60f);

            Assert.That(_events, Has.Count.EqualTo(1));
            Assert.That(_events[0].Key, Is.EqualTo(GameEvents.FieldRegionEntered));
            Assert.That(_events[0].Region, Is.EqualTo("r1"));
            Assert.That(_events[0].LayerKey, Is.EqualTo("layerX"));
            AssertRosterCount(_layer.LayerId, 1, 1);
        }

        [Test]
        public void CrossingRegion_MovesRoster_AndFiresBothEvents()
        {
            Entity unit = SpawnTracked(150, 50);   // r1
            _system.Update(1 / 60f);
            _events.Clear();

            MoveTo(unit, 450, 50);                 // cell (4,0) → r2
            _system.Update(1 / 60f);

            Assert.That(_events.Select(e => (e.Key.Value, e.Region)),
                Is.EqualTo(new[] { ("FieldRegionExited", "r1"), ("FieldRegionEntered", "r2") }));
            AssertRosterCount(_layer.LayerId, 1, 0);
            AssertRosterCount(_layer.LayerId, 2, 1);
        }

        [Test]
        public void CellChangedWithinSameRegion_NoEvents_NoRosterWrite()
        {
            Entity unit = SpawnTracked(150, 50);   // r1
            _system.Update(1 / 60f);
            _events.Clear();

            MoveTo(unit, 250, 50);                 // still r1, new cell
            _system.Update(1 / 60f);

            Assert.That(_events, Is.Empty);
            AssertRosterCount(_layer.LayerId, 1, 1);
        }

        [Test]
        public void UntrackedEntities_CostNothing()
        {
            var untracked = _world.Create(
                new MapEntity { MapId = new MapId(MapIdValue) },
                new WorldPositionCm { Value = Fix64Vec2.FromInt(150, 50) });

            _system.Update(1 / 60f);

            Assert.That(_events, Is.Empty);
            Assert.That(_world.Has<RegionMembershipCm>(untracked), Is.False);
        }

        [Test]
        public void NoMovement_SecondTickIsSilent()
        {
            SpawnTracked(150, 50);
            _system.Update(1 / 60f);
            _events.Clear();

            _system.Update(1 / 60f);
            _system.Update(1 / 60f);

            Assert.That(_events, Is.Empty);
        }

        [Test]
        public void DeathIsNotACrossing_SilentRosterRemoval()
        {
            Entity unit = SpawnTracked(150, 50);
            _system.Update(1 / 60f);
            _events.Clear();

            _world.Destroy(unit);
            _system.Update(1 / 60f);

            Assert.That(_events, Is.Empty, "destruction produces no FieldRegionExited");
            AssertRosterCount(_layer.LayerId, 1, 0);
        }

        [Test]
        public void RosterIsProjectedIntoEntityCollectionStore()
        {
            Entity first = SpawnTracked(150, 50);
            Entity second = SpawnTracked(250, 50);
            _system.Update(1 / 60f);

            Assert.That(_index.TryResolve(_layer.LayerId, 1, out Entity regionEntity), Is.True);
            string collectionKey = $"collection.field.layerX.members";
            Assert.That(_collections.TryGet(regionEntity, collectionKey, out EntityCollectionHandle handle), Is.True);
            _collections.TryGetView(handle, out EntityCollectionView view);
            Assert.That(view.Count, Is.EqualTo(2));

            var buffer = new Entity[2];
            _collections.CopyEntities(handle, 0, buffer);
            Assert.That(buffer, Is.EquivalentTo(new[] { first, second }));
        }

        [Test]
        public void PointQuery_ResolvesPositionToRegionEntity()
        {
            var cell = _layer.Field.WorldToCell(new Platform.Abstractions.WorldCmInt2(450, 50));
            int regionId = _layer.Field.Get(cell);
            Assert.That(regionId, Is.EqualTo(2));
            Assert.That(_index.TryResolve(_layer.LayerId, regionId, out Entity region), Is.True);
            Assert.That(_world.Get<RegionCm>(region).RegionId, Is.EqualTo(2));
        }

        [Test]
        public void FieldRegionEvents_AreDistinctFromTriggerRegionEvents()
        {
            Assert.That(GameEvents.FieldRegionEntered.Value, Is.EqualTo("FieldRegionEntered"));
            Assert.That(GameEvents.FieldRegionExited.Value, Is.EqualTo("FieldRegionExited"));
            Assert.That(GameEvents.FieldRegionEntered, Is.Not.EqualTo(GameEvents.RegionEntered));
            Assert.That(GameEvents.FieldRegionExited, Is.Not.EqualTo(GameEvents.RegionExited));
        }

        [Test]
        public void FootprintCursor_EnumeratesRegionCells()
        {
            var buffer = new FieldCell2D[4];
            int written = _layer.EnumerateRegionCells(1, buffer);
            Assert.That(written, Is.EqualTo(4));
            Assert.That(
                buffer,
                Is.EquivalentTo(new[] { new FieldCell2D(0, 0), new FieldCell2D(1, 0), new FieldCell2D(2, 0), new FieldCell2D(3, 0) }));

            var small = new FieldCell2D[2];
            Assert.That(_layer.EnumerateRegionCells(1, small), Is.EqualTo(2), "short buffers truncate, caller re-queries");
        }

        [Test]
        public void TrackedOnMissingLayer_FailsClosed()
        {
            var ghostLayerId = new FieldLayerId(99);
            _world.Create(
                new MapEntity { MapId = new MapId(MapIdValue) },
                new WorldPositionCm { Value = Fix64Vec2.FromInt(150, 50) },
                new FieldTrackedCm { LayerId = ghostLayerId });

            var exception = Assert.Throws<InvalidOperationException>(() => _system.Update(1 / 60f));
            Assert.That(exception!.Message, Does.Contain("99"));
        }

        private Entity SpawnTracked(int xCm, int yCm)
        {
            return _world.Create(
                new MapEntity { MapId = new MapId(MapIdValue) },
                new WorldPositionCm { Value = Fix64Vec2.FromInt(xCm, yCm) },
                new FieldTrackedCm { LayerId = _layer.LayerId });
        }

        private void MoveTo(Entity entity, int xCm, int yCm)
        {
            _world.Set(entity, new WorldPositionCm { Value = Fix64Vec2.FromInt(xCm, yCm) });
        }

        private void AssertRosterCount(FieldLayerId layerId, int regionId, int expected)
        {
            Assert.That(
                _system.TryGetRosterMemberCount(new MapId(MapIdValue), layerId, regionId, out int count) && count == expected,
                Is.True,
                $"roster (layer {layerId.Value}, region {regionId}) should hold {expected} members");
        }

        private Task Capture(EventKey key, ScriptContext context)
        {
            _events.Add((
                key,
                context.Get<string>(MapTriggerEventPayloadKeys.RegionId),
                context.Get<string>(MapTriggerEventPayloadKeys.FieldLayer)));
            return Task.CompletedTask;
        }
    }
}
