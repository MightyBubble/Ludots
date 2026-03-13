using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Ludots.Adapter.Web.Protocol;
using Ludots.Adapter.Web.Streaming;
using Ludots.Core.Presentation.Camera;
using Ludots.Core.Presentation.Rendering;
using NUnit.Framework;

namespace Ludots.Tests.ThreeC
{
    [TestFixture]
    public sealed class WebPrimitiveProtocolTests
    {
        [Test]
        public void BinaryFrameEncoder_PrimitivesIncludeStableIdsInFullFrames()
        {
            var encoder = new BinaryFrameEncoder();
            var primitives = CreatePrimitiveBuffer(
                CreatePrimitive(stableId: 101, meshAssetId: 7, x: 1f, z: 3f),
                CreatePrimitive(stableId: 202, meshAssetId: 9, x: 5f, z: 8f));

            encoder.Encode(
                frameNumber: 12,
                simTick: 34,
                timestampMs: 56,
                camera: CreateCamera(),
                primitives,
                groundOverlays: null,
                worldHud: null,
                screenHud: null,
                worldHudStrings: null,
                debugDraw: null);

            byte[] payload = CopyEncoded(encoder);
            var section = FindSection(payload, FrameProtocol.SectionPrimitives);
            PrimitiveWireRecord[] records = ReadPrimitiveRecords(payload, section.Offset, section.ItemCount);

            Assert.Multiple(() =>
            {
                Assert.That(records.Select(record => record.StableId), Is.EqualTo(new[] { 101, 202 }));
                Assert.That(records.Select(record => record.MeshAssetId), Is.EqualTo(new[] { 7, 9 }));
            });
        }

        [Test]
        public void DeltaCompressor_ReorderOnly_PublishesCurrentStableIdOrder()
        {
            var compressor = new DeltaCompressor();
            PrimitiveWireRecord a = CreatePrimitive(stableId: 101, meshAssetId: 7, x: 1f, z: 1f);
            PrimitiveWireRecord b = CreatePrimitive(stableId: 202, meshAssetId: 8, x: 2f, z: 2f);

            compressor.TryEncodeDelta(1, 1, 1, CreateCamera(), CreatePrimitiveBuffer(a, b), null, null, null, null);
            compressor.TryEncodeDelta(2, 2, 2, CreateCamera(), CreatePrimitiveBuffer(b, a), null, null, null, null);

            PrimitiveDeltaPayload delta = ReadPrimitiveDelta(CopyEncoded(compressor));
            PrimitiveWireRecord[] applied = ApplyDelta(new[] { a, b }, delta);

            Assert.Multiple(() =>
            {
                Assert.That(delta.ChangedCount, Is.EqualTo(0), "Pure reorder should not generate item changes.");
                Assert.That(delta.RemovedStableIds, Is.Empty);
                Assert.That(delta.OrderedStableIds, Is.EqualTo(new[] { 202, 101 }));
                Assert.That(applied.Select(record => record.StableId), Is.EqualTo(new[] { 202, 101 }));
            });
        }

        [Test]
        public void DeltaCompressor_HideShowSpawnAndDespawn_ReplaysByStableId()
        {
            var compressor = new DeltaCompressor();
            PrimitiveWireRecord a1 = CreatePrimitive(stableId: 101, meshAssetId: 7, x: 1f, z: 1f);
            PrimitiveWireRecord b = CreatePrimitive(stableId: 202, meshAssetId: 8, x: 2f, z: 2f);
            PrimitiveWireRecord a2 = CreatePrimitive(stableId: 101, meshAssetId: 7, x: 4f, z: 4f);
            PrimitiveWireRecord c = CreatePrimitive(stableId: 303, meshAssetId: 9, x: 6f, z: 6f);

            PrimitiveWireRecord[] current = { a1, b };
            compressor.TryEncodeDelta(1, 1, 1, CreateCamera(), CreatePrimitiveBuffer(current), null, null, null, null);

            compressor.TryEncodeDelta(2, 2, 2, CreateCamera(), CreatePrimitiveBuffer(b), null, null, null, null);
            current = ApplyDelta(current, ReadPrimitiveDelta(CopyEncoded(compressor)));
            Assert.That(current.Select(record => record.StableId), Is.EqualTo(new[] { 202 }));

            compressor.TryEncodeDelta(3, 3, 3, CreateCamera(), CreatePrimitiveBuffer(b, c), null, null, null, null);
            current = ApplyDelta(current, ReadPrimitiveDelta(CopyEncoded(compressor)));
            Assert.That(current.Select(record => record.StableId), Is.EqualTo(new[] { 202, 303 }));

            compressor.TryEncodeDelta(4, 4, 4, CreateCamera(), CreatePrimitiveBuffer(a2, c), null, null, null, null);
            current = ApplyDelta(current, ReadPrimitiveDelta(CopyEncoded(compressor)));

            Assert.Multiple(() =>
            {
                Assert.That(current.Select(record => record.StableId), Is.EqualTo(new[] { 101, 303 }));
                Assert.That(current[0].PosX, Is.EqualTo(4f).Within(0.001f), "Reintroduced stable ids should use the new payload instead of stale array slots.");
                Assert.That(current[1].StableId, Is.EqualTo(303));
            });
        }

        [Test]
        public void DeltaCompressor_UnstablePrimitives_FallsBackToFullPrimitiveReplacement()
        {
            var compressor = new DeltaCompressor();
            PrimitiveWireRecord stable = CreatePrimitive(stableId: 101, meshAssetId: 7, x: 1f, z: 1f);
            PrimitiveWireRecord unstable = CreatePrimitive(stableId: 0, meshAssetId: 8, x: 9f, z: 9f);

            compressor.TryEncodeDelta(1, 1, 1, CreateCamera(), CreatePrimitiveBuffer(stable), null, null, null, null);
            compressor.TryEncodeDelta(2, 2, 2, CreateCamera(), CreatePrimitiveBuffer(unstable), null, null, null, null);

            byte[] payload = CopyEncoded(compressor);
            var primitivesSection = FindSection(payload, FrameProtocol.SectionPrimitives);
            PrimitiveWireRecord[] records = ReadPrimitiveRecords(payload, primitivesSection.Offset, primitivesSection.ItemCount);

            Assert.Multiple(() =>
            {
                Assert.That(HasSection(payload, FrameProtocol.SectionPrimitivesDelta), Is.False);
                Assert.That(records.Select(record => record.StableId), Is.EqualTo(new[] { 0 }));
                Assert.That(records[0].PosX, Is.EqualTo(9f).Within(0.001f));
            });
        }

        private static CameraRenderState3D CreateCamera()
        {
            return new CameraRenderState3D(new Vector3(1f, 2f, 3f), new Vector3(0f, 0f, 0f), Vector3.UnitY, 60f);
        }

        private static PrimitiveDrawBuffer CreatePrimitiveBuffer(params PrimitiveWireRecord[] records)
        {
            var buffer = new PrimitiveDrawBuffer(records.Length + 4);
            for (int i = 0; i < records.Length; i++)
            {
                buffer.TryAdd(records[i].ToRuntimeItem());
            }

            return buffer;
        }

        private static byte[] CopyEncoded(BinaryFrameEncoder encoder)
        {
            var payload = new byte[encoder.EncodedLength];
            encoder.CopyTo(payload);
            return payload;
        }

        private static byte[] CopyEncoded(DeltaCompressor compressor)
        {
            var payload = new byte[compressor.EncodedLength];
            compressor.CopyTo(payload);
            return payload;
        }

        private static SectionHeader FindSection(byte[] payload, byte sectionType)
        {
            int p = FrameProtocol.FrameHeaderSize;
            while (p < payload.Length)
            {
                byte currentSection = payload[p];
                if (currentSection == FrameProtocol.SectionEnd)
                {
                    break;
                }

                ushort itemCount = BinaryPrimitives.ReadUInt16LittleEndian(payload.AsSpan(p + 1, 2));
                int byteLength = BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(p + 3, 4));
                if (currentSection == sectionType)
                {
                    return new SectionHeader(p + FrameProtocol.SectionHeaderSize, itemCount, byteLength);
                }

                p += FrameProtocol.SectionHeaderSize + byteLength;
            }

            throw new AssertionException($"Section 0x{sectionType:X2} not found.");
        }

        private static bool HasSection(byte[] payload, byte sectionType)
        {
            int p = FrameProtocol.FrameHeaderSize;
            while (p < payload.Length)
            {
                byte currentSection = payload[p];
                if (currentSection == FrameProtocol.SectionEnd)
                {
                    return false;
                }

                int byteLength = BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(p + 3, 4));
                if (currentSection == sectionType)
                {
                    return true;
                }

                p += FrameProtocol.SectionHeaderSize + byteLength;
            }

            return false;
        }

        private static PrimitiveWireRecord[] ReadPrimitiveRecords(byte[] payload, int offset, int count)
        {
            var records = new PrimitiveWireRecord[count];
            int p = offset;
            for (int i = 0; i < count; i++)
            {
                records[i] = ReadPrimitiveRecord(payload, p);
                p += WirePrimitiveDrawItem.SizeInBytes;
            }

            return records;
        }

        private static PrimitiveDeltaPayload ReadPrimitiveDelta(byte[] payload)
        {
            var section = FindSection(payload, FrameProtocol.SectionPrimitivesDelta);
            int p = section.Offset;
            ushort totalCount = BinaryPrimitives.ReadUInt16LittleEndian(payload.AsSpan(p, 2));
            ushort removedCount = BinaryPrimitives.ReadUInt16LittleEndian(payload.AsSpan(p + 2, 2));
            p += 4;

            var removed = new int[removedCount];
            for (int i = 0; i < removedCount; i++)
            {
                removed[i] = BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(p, 4));
                p += 4;
            }

            var changed = new PrimitiveWireRecord[section.ItemCount];
            for (int i = 0; i < section.ItemCount; i++)
            {
                changed[i] = ReadPrimitiveRecord(payload, p);
                p += WirePrimitiveDrawItem.SizeInBytes;
            }

            var orderedStableIds = new int[totalCount];
            for (int i = 0; i < totalCount; i++)
            {
                orderedStableIds[i] = BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(p, 4));
                p += 4;
            }

            return new PrimitiveDeltaPayload(section.ItemCount, removed, changed, orderedStableIds);
        }

        private static PrimitiveWireRecord[] ApplyDelta(
            IReadOnlyCollection<PrimitiveWireRecord> previous,
            PrimitiveDeltaPayload delta)
        {
            var byStableId = new Dictionary<int, PrimitiveWireRecord>(previous.Count);
            foreach (var item in previous)
            {
                if (item.StableId > 0)
                {
                    byStableId[item.StableId] = item;
                }
            }

            for (int i = 0; i < delta.RemovedStableIds.Length; i++)
            {
                byStableId.Remove(delta.RemovedStableIds[i]);
            }

            for (int i = 0; i < delta.ChangedItems.Length; i++)
            {
                PrimitiveWireRecord item = delta.ChangedItems[i];
                if (item.StableId > 0)
                {
                    byStableId[item.StableId] = item;
                }
            }

            var ordered = new PrimitiveWireRecord[delta.OrderedStableIds.Length];
            for (int i = 0; i < delta.OrderedStableIds.Length; i++)
            {
                ordered[i] = byStableId[delta.OrderedStableIds[i]];
            }

            return ordered;
        }

        private static PrimitiveWireRecord ReadPrimitiveRecord(byte[] payload, int offset)
        {
            return new PrimitiveWireRecord(
                MeshAssetId: BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(offset, 4)),
                StableId: BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(offset + 4, 4)),
                PosX: BinaryPrimitives.ReadSingleLittleEndian(payload.AsSpan(offset + 8, 4)),
                PosY: BinaryPrimitives.ReadSingleLittleEndian(payload.AsSpan(offset + 12, 4)),
                PosZ: BinaryPrimitives.ReadSingleLittleEndian(payload.AsSpan(offset + 16, 4)),
                ScaleX: BinaryPrimitives.ReadSingleLittleEndian(payload.AsSpan(offset + 20, 4)),
                ScaleY: BinaryPrimitives.ReadSingleLittleEndian(payload.AsSpan(offset + 24, 4)),
                ScaleZ: BinaryPrimitives.ReadSingleLittleEndian(payload.AsSpan(offset + 28, 4)),
                R: BinaryPrimitives.ReadSingleLittleEndian(payload.AsSpan(offset + 32, 4)),
                G: BinaryPrimitives.ReadSingleLittleEndian(payload.AsSpan(offset + 36, 4)),
                B: BinaryPrimitives.ReadSingleLittleEndian(payload.AsSpan(offset + 40, 4)),
                A: BinaryPrimitives.ReadSingleLittleEndian(payload.AsSpan(offset + 44, 4)));
        }

        private static PrimitiveWireRecord CreatePrimitive(int stableId, int meshAssetId, float x, float z)
        {
            return new PrimitiveWireRecord(
                MeshAssetId: meshAssetId,
                StableId: stableId,
                PosX: x,
                PosY: 0.5f,
                PosZ: z,
                ScaleX: 1f,
                ScaleY: 1f,
                ScaleZ: 1f,
                R: 0.25f,
                G: 0.5f,
                B: 0.75f,
                A: 1f);
        }

        private readonly record struct SectionHeader(int Offset, ushort ItemCount, int ByteLength);

        private readonly record struct PrimitiveDeltaPayload(
            int ChangedCount,
            int[] RemovedStableIds,
            PrimitiveWireRecord[] ChangedItems,
            int[] OrderedStableIds);

        private readonly record struct PrimitiveWireRecord(
            int MeshAssetId,
            int StableId,
            float PosX,
            float PosY,
            float PosZ,
            float ScaleX,
            float ScaleY,
            float ScaleZ,
            float R,
            float G,
            float B,
            float A)
        {
            public PrimitiveDrawItem ToRuntimeItem()
            {
                return new PrimitiveDrawItem
                {
                    MeshAssetId = MeshAssetId,
                    StableId = StableId,
                    Position = new Vector3(PosX, PosY, PosZ),
                    Scale = new Vector3(ScaleX, ScaleY, ScaleZ),
                    Color = new Vector4(R, G, B, A),
                };
            }
        }
    }
}
