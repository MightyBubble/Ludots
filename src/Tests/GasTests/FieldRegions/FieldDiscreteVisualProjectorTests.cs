using System;
using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Fields;
using Ludots.Core.Fields.Config;
using Ludots.Core.Gameplay.FieldRegions;
using Ludots.Core.Map;
using Ludots.Core.Presentation.Rendering;
using NUnit.Framework;

namespace Ludots.Tests.GAS.FieldRegions
{
    [TestFixture]
    public sealed class FieldDiscreteVisualProjectorTests
    {
        private World _world = null!;
        private MapSession _session = null!;
        private DiscreteIdFieldLayerData _layer = null!;

        [SetUp]
        public void SetUp()
        {
            _world = World.Create();
            var catalog = new FieldLayerRegistry();
            catalog.Register(
                "ownership.test",
                FieldLayerKind.DiscreteId,
                cellSizeCm: 100,
                chunkSizeCells: 8,
                FieldLayerDefaultValue.None,
                persistent: true,
                "test.writer",
                maxRegionIds: 16);
            _session = new MapSession(new MapId("map_projection_test"), new MapConfig());
            _session.Fields = FieldSessionStore.Create(catalog, new[] { "ownership.test" });
            _layer = _session.Fields.Get<DiscreteIdFieldLayerData>(catalog.GetId("ownership.test"));
            foreach (string key in new[] { "zone.a1", "zone.a2", "zone.b1" })
            {
                _layer.Regions.Register(key);
            }

            _layer.Field.Set(new FieldCell2D(0, 0), 1);
            _layer.Field.Set(new FieldCell2D(3, 0), 2);
            _layer.Field.Set(new FieldCell2D(9, 0), 3);
            _session.RegionIndex = FieldRegionMaterializer.Materialize(_world, _session);
        }

        [TearDown]
        public void TearDown()
        {
            _session.Dispose();
            _world.Dispose();
        }

        [Test]
        public void HierarchyRemap_ResolvesLeafExactDepthAndGroupKey()
        {
            RegionHierarchyRuntime hierarchy = RegionHierarchyBuilder.Build(
                _world,
                _session,
                new List<FieldHierarchyRoster>
                {
                    new("group.top", new List<string> { "group.mid", "zone.b1" }),
                    new("group.mid", new List<string> { "zone.a1", "zone.a2" }),
                });
            FieldDiscreteVisualMapMode leaf = FieldDiscreteVisualMapMode.Leaf;
            FieldDiscreteVisualMapMode parent = FieldDiscreteVisualMapMode.AncestorDepth(1);
            FieldDiscreteVisualMapMode top = FieldDiscreteVisualMapMode.AncestorDepth(2);
            FieldDiscreteVisualMapMode midGroup = FieldDiscreteVisualMapMode.Group("group.mid");

            Assert.That(hierarchy.ResolveProjectedId(_layer.LayerId, 1, in leaf), Is.EqualTo(1));
            Assert.That(
                hierarchy.ResolveProjectedId(_layer.LayerId, 1, in parent),
                Is.EqualTo(hierarchy.ResolveProjectedId(_layer.LayerId, 2, in parent)));
            Assert.That(hierarchy.ResolveProjectedId(_layer.LayerId, 1, in top), Is.Not.Zero);
            Assert.That(hierarchy.ResolveProjectedId(_layer.LayerId, 3, in top), Is.Zero);
            Assert.That(hierarchy.ResolveProjectedId(_layer.LayerId, 1, in midGroup), Is.Not.Zero);
            Assert.That(hierarchy.ResolveProjectedId(_layer.LayerId, 3, in midGroup), Is.Zero);
        }

        [Test]
        public void Project_UsesDirtyCursorToPublishOnlyChangedChunkAfterBake()
        {
            var projector = new FieldDiscreteVisualProjector();
            var buffer = new GlobalFieldVisualBuffer(
                recordCapacity: 2,
                cellCapacity: 64,
                dirtyRectCapacity: 4);
            FieldDiscreteVisualMapMode leaf = FieldDiscreteVisualMapMode.Leaf;

            buffer.BeginFrame();
            projector.Project(1, _session.Fields!, null, in leaf, buffer);
            Assert.That(projector.LastFullProjectionCount, Is.EqualTo(1));
            Assert.That(projector.LastProjectedCellCount, Is.EqualTo(3));

            _layer.Field.Set(new FieldCell2D(0, 0), 2);
            buffer.BeginFrame();
            projector.Project(1, _session.Fields!, null, in leaf, buffer);

            Assert.That(projector.LastFullProjectionCount, Is.EqualTo(0));
            Assert.That(projector.LastProjectedDirtyRectCount, Is.EqualTo(1));
            Assert.That(buffer.GetDirtyRects(ActiveRecord(buffer))[0], Is.EqualTo(new Ludots.Core.Mathematics.IntRect(0, 0, 8, 8)));
            Assert.That(FindByte(buffer, new FieldCell2D(0, 0)), Is.EqualTo(2));
        }

        [Test]
        public void Project_UsesVectorPaletteWhenRegionIdExceedsByteCapacity()
        {
            var catalog = new FieldLayerRegistry();
            catalog.Register(
                "ownership.large",
                FieldLayerKind.DiscreteId,
                cellSizeCm: 100,
                chunkSizeCells: 8,
                FieldLayerDefaultValue.None,
                persistent: true,
                "test.writer",
                maxRegionIds: 256);
            FieldSessionStore store = FieldSessionStore.Create(catalog, new[] { "ownership.large" });
            DiscreteIdFieldLayerData layer =
                store.Get<DiscreteIdFieldLayerData>(catalog.GetId("ownership.large"));
            for (int regionId = 1; regionId <= 256; regionId++)
            {
                layer.Regions.Register($"region.{regionId}");
            }

            layer.Field.Set(new FieldCell2D(0, 0), 256);
            Vector4 expected = new(0.1f, 0.2f, 0.3f, 0.4f);
            var projector = new FieldDiscreteVisualProjector(_ => expected);
            var buffer = new GlobalFieldVisualBuffer(2, 8, 2);
            FieldDiscreteVisualMapMode leaf = FieldDiscreteVisualMapMode.Leaf;

            buffer.BeginFrame();
            projector.Project(1, store, null, in leaf, buffer);

            GlobalFieldVisualRecord record = ActiveRecord(buffer);
            Assert.That(record.Descriptor.ValueKind, Is.EqualTo(GlobalFieldVisualValueKind.Vector4));
            Assert.That(buffer.GetCells(record)[0].FloatValue, Is.EqualTo(expected));
        }

        private static GlobalFieldVisualRecord ActiveRecord(GlobalFieldVisualBuffer buffer)
        {
            ReadOnlySpan<GlobalFieldVisualRecord> records = buffer.GetRecords();
            for (int i = 0; i < records.Length; i++)
            {
                if (records[i].IsActive &&
                    records[i].Descriptor.Id.Kind == GlobalFieldVisualKind.DiscreteOwnership)
                {
                    return records[i];
                }
            }

            throw new AssertionException("Discrete ownership record was not active.");
        }

        private static byte FindByte(GlobalFieldVisualBuffer buffer, FieldCell2D target)
        {
            GlobalFieldVisualRecord record = ActiveRecord(buffer);
            ReadOnlySpan<GlobalFieldVisualCell> cells = buffer.GetCells(record);
            for (int i = 0; i < cells.Length; i++)
            {
                if (cells[i].Cell == target)
                {
                    return cells[i].ByteValue;
                }
            }

            throw new AssertionException($"Cell {target} was not projected.");
        }
    }
}
