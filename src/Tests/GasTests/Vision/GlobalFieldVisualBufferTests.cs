using System;
using System.Linq;
using Ludots.Core.Fields;
using Ludots.Core.Mathematics;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Vision;
using NUnit.Framework;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    public sealed class GlobalFieldVisualBufferTests
    {
        [Test]
        public void GlobalFieldVisualBuffer_StoresActiveRecordsCellsDirtyRectsAndLifecycle()
        {
            var buffer = new GlobalFieldVisualBuffer(recordCapacity: 2, cellCapacity: 4, dirtyRectCapacity: 2);
            Span<GlobalFieldVisualCell> cells = stackalloc GlobalFieldVisualCell[2];
            cells[0] = new GlobalFieldVisualCell(new FieldCell2D(0, 0), 2);
            cells[1] = new GlobalFieldVisualCell(new FieldCell2D(1, 0), 1);
            Span<IntRect> dirty = stackalloc IntRect[1];
            dirty[0] = new IntRect(0, 0, 2, 1);

            buffer.BeginFrame();
            int index = buffer.Upsert(
                CreateDescriptor(scopeKeyId: 7, layerKeyId: 3, bounds: new IntRect(0, 0, 2, 1)),
                cells,
                dirty);

            Assert.That(index, Is.EqualTo(0));
            Assert.That(buffer.ProjectionRevision, Is.EqualTo(1));
            Assert.That(buffer.RecordCount, Is.EqualTo(1));
            Assert.That(buffer.ActiveRecordCount, Is.EqualTo(1));
            Assert.That(buffer.CellCount, Is.EqualTo(2));
            Assert.That(buffer.DirtyRectCount, Is.EqualTo(1));

            ReadOnlySpan<GlobalFieldVisualRecord> records = buffer.GetRecords();
            ref readonly GlobalFieldVisualRecord record = ref records[0];
            Assert.That(record.IsActive, Is.True);
            Assert.That(record.Revision, Is.EqualTo(1));
            Assert.That(record.Descriptor.Id.Kind, Is.EqualTo(GlobalFieldVisualKind.Fog));
            Assert.That(record.Descriptor.Id.ScopeKeyId, Is.EqualTo(7));
            Assert.That(record.Descriptor.Id.LayerKeyId, Is.EqualTo(3));
            Assert.That(record.Descriptor.BoundsCells, Is.EqualTo(new IntRect(0, 0, 2, 1)));
            Assert.That(buffer.GetCells(record)[1].ByteValue, Is.EqualTo(1));
            Assert.That(buffer.GetDirtyRects(record)[0], Is.EqualTo(new IntRect(0, 0, 2, 1)));

            buffer.BeginFrame();

            records = buffer.GetRecords();
            Assert.That(buffer.ProjectionRevision, Is.EqualTo(2));
            Assert.That(buffer.RecordCount, Is.EqualTo(1));
            Assert.That(buffer.ActiveRecordCount, Is.EqualTo(0));
            Assert.That(records[0].IsActive, Is.False);
            Assert.That(buffer.CellCount, Is.EqualTo(0));
            Assert.That(buffer.DirtyRectCount, Is.EqualTo(0));
        }

        [Test]
        public void GlobalFieldVisualBuffer_ThrowsOnOverflowAndDuplicateWrites()
        {
            var twoCells = new[]
            {
                new GlobalFieldVisualCell(new FieldCell2D(0, 0), 1),
                new GlobalFieldVisualCell(new FieldCell2D(1, 0), 1),
            };
            var oneCell = new[]
            {
                new GlobalFieldVisualCell(new FieldCell2D(0, 0), 1),
            };
            var oneDirty = new[]
            {
                new IntRect(0, 0, 1, 1),
            };
            var twoDirty = new[]
            {
                new IntRect(0, 0, 1, 1),
                new IntRect(1, 0, 1, 1),
            };

            var cellOverflow = new GlobalFieldVisualBuffer(recordCapacity: 1, cellCapacity: 1, dirtyRectCapacity: 1);
            cellOverflow.BeginFrame();
            Assert.That(
                () => cellOverflow.Upsert(CreateDescriptor(1, 1, new IntRect(0, 0, 2, 1)), twoCells, ReadOnlySpan<IntRect>.Empty),
                Throws.InvalidOperationException.With.Message.Contains("cell capacity"));

            var dirtyOverflow = new GlobalFieldVisualBuffer(recordCapacity: 1, cellCapacity: 1, dirtyRectCapacity: 1);
            dirtyOverflow.BeginFrame();
            Assert.That(
                () => dirtyOverflow.Upsert(CreateDescriptor(1, 1, new IntRect(0, 0, 1, 1)), oneCell, twoDirty),
                Throws.InvalidOperationException.With.Message.Contains("dirty-rect capacity"));

            var recordOverflow = new GlobalFieldVisualBuffer(recordCapacity: 1, cellCapacity: 2, dirtyRectCapacity: 1);
            recordOverflow.BeginFrame();
            recordOverflow.Upsert(CreateDescriptor(1, 1, new IntRect(0, 0, 1, 1)), oneCell, ReadOnlySpan<IntRect>.Empty);
            Assert.That(
                () => recordOverflow.Upsert(CreateDescriptor(2, 1, new IntRect(0, 0, 1, 1)), oneCell, ReadOnlySpan<IntRect>.Empty),
                Throws.InvalidOperationException.With.Message.Contains("record capacity"));

            var duplicate = new GlobalFieldVisualBuffer(recordCapacity: 1, cellCapacity: 2, dirtyRectCapacity: 1);
            duplicate.BeginFrame();
            duplicate.Upsert(CreateDescriptor(1, 1, new IntRect(0, 0, 1, 1)), oneCell, oneDirty);
            Assert.That(
                () => duplicate.Upsert(CreateDescriptor(1, 1, new IntRect(0, 0, 1, 1)), oneCell, ReadOnlySpan<IntRect>.Empty),
                Throws.InvalidOperationException.With.Message.Contains("more than once"));
        }

        [Test]
        public void GlobalFieldVisualBuffer_PublicContractDoesNotExposeRaylibTypes()
        {
            Type[] types =
            {
                typeof(GlobalFieldVisualBuffer),
                typeof(GlobalFieldVisualId),
                typeof(GlobalFieldVisualDescriptor),
                typeof(GlobalFieldVisualCell),
                typeof(GlobalFieldVisualRecord),
                typeof(FieldCell2D),
                typeof(FieldGridSpec2D),
                typeof(FieldChunk2D<byte>),
                typeof(ChunkedField2D<byte>),
            };

            foreach (Type type in types)
            {
                foreach (Type memberType in type.GetFields().Select(f => f.FieldType)
                             .Concat(type.GetProperties().Select(p => p.PropertyType)))
                {
                    Assert.That(memberType.FullName, Does.Not.Contain("Raylib"), $"{type.Name} exposes {memberType.FullName}.");
                }
            }
        }

        [Test]
        public void GlobalFieldVisualBuffer_AcceptsSharedFieldKindsWithoutRendererSpecificContracts()
        {
            var buffer = new GlobalFieldVisualBuffer(recordCapacity: 3, cellCapacity: 3, dirtyRectCapacity: 3);
            buffer.BeginFrame();

            UpsertVectorField(buffer, GlobalFieldVisualKind.Weather, surfaceKeyId: 1);
            UpsertVectorField(buffer, GlobalFieldVisualKind.Water, surfaceKeyId: 2);
            UpsertVectorField(buffer, GlobalFieldVisualKind.Influence, surfaceKeyId: 3);

            Assert.That(buffer.ActiveRecordCount, Is.EqualTo(3));
            ReadOnlySpan<GlobalFieldVisualRecord> records = buffer.GetRecords();
            Assert.That(records[0].Descriptor.ValueKind, Is.EqualTo(GlobalFieldVisualValueKind.Vector4));
            Assert.That(records[1].Descriptor.Id.Kind, Is.EqualTo(GlobalFieldVisualKind.Water));
            Assert.That(records[2].Descriptor.Id.Kind, Is.EqualTo(GlobalFieldVisualKind.Influence));
        }

        [Test]
        public void FogGlobalFieldVisualProjector_ProjectsFogStoreIntoBackendAgnosticBuffer()
        {
            FogFieldStore store = CreateFogStore(out FogLayerId layerId);
            Assert.That(store.TryGet(5, layerId, out FogField field), Is.True);
            field.SetVisible(new FogCell(0, 0));
            field.SetExplored(new FogCell(1, 0));
            field.SetDenied(new FogCell(-1, 2));

            var buffer = new GlobalFieldVisualBuffer(recordCapacity: 4, cellCapacity: 16, dirtyRectCapacity: 4);
            var projector = new FogGlobalFieldVisualProjector();
            buffer.BeginFrame();
            projector.Project(store, buffer);

            Assert.That(projector.LastProjectedFieldCount, Is.EqualTo(1));
            Assert.That(projector.LastProjectedCellCount, Is.EqualTo(3));
            Assert.That(projector.LastProjectedDirtyRectCount, Is.EqualTo(1));
            Assert.That(buffer.ActiveRecordCount, Is.EqualTo(1));

            ReadOnlySpan<GlobalFieldVisualRecord> records = buffer.GetRecords();
            ref readonly GlobalFieldVisualRecord record = ref records[0];
            Assert.That(record.Descriptor.Id.Kind, Is.EqualTo(GlobalFieldVisualKind.Fog));
            Assert.That(record.Descriptor.Id.ScopeKeyId, Is.EqualTo(5));
            Assert.That(record.Descriptor.Id.LayerKeyId, Is.EqualTo(layerId.Value));
            Assert.That(record.Descriptor.CellSizeCm, Is.EqualTo(100));
            Assert.That(record.Descriptor.ValueKind, Is.EqualTo(GlobalFieldVisualValueKind.Byte));
            Assert.That(record.Descriptor.BoundsCells, Is.EqualTo(new IntRect(-1, 0, 3, 3)));
            Assert.That(buffer.GetDirtyRects(record)[0], Is.EqualTo(new IntRect(-1, 0, 3, 3)));
            Assert.That(field.DirtyCount, Is.EqualTo(0));

            ReadOnlySpan<GlobalFieldVisualCell> cells = buffer.GetCells(record);
            Assert.That(FindByte(cells, new FieldCell2D(0, 0)), Is.EqualTo((byte)CellVisibility.Visible));
            Assert.That(FindByte(cells, new FieldCell2D(1, 0)), Is.EqualTo((byte)CellVisibility.Explored));
            Assert.That(FindByte(cells, new FieldCell2D(-1, 2)), Is.EqualTo((byte)CellVisibility.Denied));
        }

        [Test]
        public void FogGlobalFieldVisualProjector_PreservesPriorBoundsWhenCellsShrink()
        {
            FogFieldStore store = CreateFogStore(out FogLayerId layerId);
            Assert.That(store.TryGet(5, layerId, out FogField field), Is.True);
            field.SetVisible(new FogCell(0, 0));
            field.SetVisible(new FogCell(8, 8));

            var buffer = new GlobalFieldVisualBuffer(recordCapacity: 4, cellCapacity: 16, dirtyRectCapacity: 4);
            var projector = new FogGlobalFieldVisualProjector();
            buffer.BeginFrame();
            projector.Project(store, buffer);

            field.SetVisibility(new FogCell(8, 8), CellVisibility.Unseen);
            buffer.BeginFrame();
            projector.Project(store, buffer);

            ref readonly GlobalFieldVisualRecord record = ref buffer.GetRecords()[0];
            Assert.That(record.Descriptor.BoundsCells, Is.EqualTo(new IntRect(0, 0, 9, 9)));
            Assert.That(buffer.GetDirtyRects(record)[0], Is.EqualTo(new IntRect(8, 8, 1, 1)));
            Assert.That(buffer.GetCells(record).Length, Is.EqualTo(1));
            Assert.That(field.DirtyCount, Is.EqualTo(0));
        }

        [Test]
        public void FogGlobalFieldVisualProjector_ReusesScratchAndAllocatesZeroAfterWarmup()
        {
            FogFieldStore store = CreateFogStore(out FogLayerId layerId);
            Assert.That(store.TryGet(5, layerId, out FogField field), Is.True);
            for (int i = 0; i < 256; i++)
            {
                field.SetExplored(new FogCell(i & 15, i >> 4));
            }

            var buffer = new GlobalFieldVisualBuffer(recordCapacity: 4, cellCapacity: 512, dirtyRectCapacity: 4);
            var projector = new FogGlobalFieldVisualProjector();
            buffer.BeginFrame();
            projector.Project(store, buffer);
            buffer.BeginFrame();
            projector.Project(store, buffer);

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            long allocated = MeasureFogProjectionAllocations(
                projector,
                store,
                buffer,
                out int observed);
            Assert.That(observed, Is.EqualTo(256_000));
            Assert.That(allocated, Is.EqualTo(0));
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static long MeasureFogProjectionAllocations(
            FogGlobalFieldVisualProjector projector,
            FogFieldStore store,
            GlobalFieldVisualBuffer buffer,
            out int observed)
        {
            GC.GetAllocatedBytesForCurrentThread();
            long before = GC.GetAllocatedBytesForCurrentThread();
            observed = 0;
            for (int i = 0; i < 1_000; i++)
            {
                buffer.BeginFrame();
                projector.Project(store, buffer);
                observed += buffer.CellCount;
            }

            return GC.GetAllocatedBytesForCurrentThread() - before;
        }

        private static GlobalFieldVisualDescriptor CreateDescriptor(int scopeKeyId, int layerKeyId, IntRect bounds)
        {
            return new GlobalFieldVisualDescriptor(
                new GlobalFieldVisualId(GlobalFieldVisualKind.Fog, scopeKeyId, layerKeyId, surfaceKeyId: 0),
                cellSizeCm: 100,
                WorldCmInt2.Zero,
                bounds,
                GlobalFieldVisualValueKind.Byte);
        }

        private static void UpsertVectorField(
            GlobalFieldVisualBuffer buffer,
            GlobalFieldVisualKind kind,
            int surfaceKeyId)
        {
            Span<GlobalFieldVisualCell> cells = stackalloc GlobalFieldVisualCell[1];
            cells[0] = new GlobalFieldVisualCell(new FieldCell2D(surfaceKeyId, 0), new System.Numerics.Vector4(1f, 2f, 3f, 4f));
            Span<IntRect> dirty = stackalloc IntRect[1];
            dirty[0] = new IntRect(surfaceKeyId, 0, 1, 1);
            buffer.Upsert(
                new GlobalFieldVisualDescriptor(
                    new GlobalFieldVisualId(kind, scopeKeyId: 1, layerKeyId: 0, surfaceKeyId),
                    cellSizeCm: 100,
                    WorldCmInt2.Zero,
                    new IntRect(surfaceKeyId, 0, 1, 1),
                    GlobalFieldVisualValueKind.Vector4),
                cells,
                dirty);
        }

        private static FogFieldStore CreateFogStore(out FogLayerId layerId)
        {
            var registry = new FogLayerRegistry();
            layerId = registry.Register("ground", cellSizeCm: 100, updateHz: 10);
            var store = new FogFieldStore(initialCapacity: 2, chunkSizeCells: 16);
            store.GetOrCreate(5, registry.Get(layerId));
            return store;
        }

        private static byte FindByte(ReadOnlySpan<GlobalFieldVisualCell> cells, FieldCell2D target)
        {
            for (int i = 0; i < cells.Length; i++)
            {
                if (cells[i].Cell == target)
                {
                    return cells[i].ByteValue;
                }
            }

            throw new InvalidOperationException($"Cell {target} was not projected.");
        }
    }
}
