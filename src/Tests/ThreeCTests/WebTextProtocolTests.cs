using System;
using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using Ludots.Adapter.Web.Protocol;
using Ludots.Adapter.Web.Streaming;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Camera;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Config;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Performers;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Registry;
using NUnit.Framework;

namespace Ludots.Tests.ThreeC
{
    [TestFixture]
    public sealed class WebTextProtocolTests
    {
        [Test]
        public void BinaryFrameEncoder_ScreenHud_EncodesPresentationTextPacketAndTemplateTable()
        {
            var screenHud = new ScreenHudBatchBuffer(4);
            var strings = CreateWorldHudStrings("{0}/{1}");
            var packet = PresentationTextPacket.FromToken(1);
            packet.SetArg(0, PresentationTextArg.FromInt32(100));
            packet.SetArg(1, PresentationTextArg.FromInt32(150));

            screenHud.TryAdd(new ScreenHudItem
            {
                Kind = WorldHudItemKind.Text,
                ScreenX = 320f,
                ScreenY = 180f,
                FontSize = 16,
                Text = packet,
            });

            var encoder = new BinaryFrameEncoder();
            var camera = new CameraRenderState3D(Vector3.Zero, Vector3.UnitZ, Vector3.UnitY, 60f);
            encoder.Encode(1, 2, 3, in camera, null, null, null, screenHud, strings, null, null, null);

            ReadOnlySpan<byte> buffer = encoder.GetResult();
            var (payloadOffset, itemCount, _) = FindSection(buffer, FrameProtocol.SectionScreenHud);
            Assert.That(itemCount, Is.EqualTo(1));

            int itemOffset = payloadOffset;
            Assert.That(buffer[itemOffset], Is.EqualTo((byte)WorldHudItemKind.Text));
            Assert.That(BinaryPrimitives.ReadInt32LittleEndian(buffer.Slice(itemOffset + 73, 4)), Is.EqualTo(1));
            Assert.That(buffer[itemOffset + 77], Is.EqualTo(2));
            Assert.That(buffer[itemOffset + 81], Is.EqualTo((byte)PresentationTextArgType.Int32));
            Assert.That(BinaryPrimitives.ReadInt32LittleEndian(buffer.Slice(itemOffset + 85, 4)), Is.EqualTo(100));
            Assert.That(buffer[itemOffset + 89], Is.EqualTo((byte)PresentationTextArgType.Int32));
            Assert.That(BinaryPrimitives.ReadInt32LittleEndian(buffer.Slice(itemOffset + 93, 4)), Is.EqualTo(150));

            int cursor = itemOffset + WireWorldHudItem.SizeInBytes;
            int stringCount = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(cursor, 2));
            Assert.That(stringCount, Is.EqualTo(0));
            cursor += 2;

            int templateCount = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(cursor, 2));
            Assert.That(templateCount, Is.EqualTo(1));
            cursor += 2;

            int tokenId = BinaryPrimitives.ReadInt32LittleEndian(buffer.Slice(cursor, 4));
            cursor += 4;
            int templateByteCount = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(cursor, 2));
            cursor += 2;
            string template = Encoding.UTF8.GetString(buffer.Slice(cursor, templateByteCount));

            Assert.That(tokenId, Is.EqualTo(1));
            Assert.That(template, Is.EqualTo("{0}/{1}"));
        }

        [Test]
        public void BinaryFrameEncoder_ScreenOverlay_EncodesPresentationTextPacketAndTemplateTable()
        {
            var overlay = new ScreenOverlayBuffer();
            var strings = CreateWorldHudStrings("READY {0}");
            var packet = PresentationTextPacket.FromToken(1);
            packet.SetArg(0, PresentationTextArg.FromInt32(7));
            overlay.AddText(12, 24, in packet, 18, new Vector4(1f, 1f, 1f, 1f));

            var encoder = new BinaryFrameEncoder();
            var camera = new CameraRenderState3D(Vector3.Zero, Vector3.UnitZ, Vector3.UnitY, 60f);
            encoder.Encode(1, 2, 3, in camera, null, null, null, null, strings, null, overlay, null);

            ReadOnlySpan<byte> buffer = encoder.GetResult();
            var (payloadOffset, itemCount, _) = FindSection(buffer, FrameProtocol.SectionScreenOverlay);
            Assert.That(itemCount, Is.EqualTo(1));

            int itemOffset = payloadOffset;
            Assert.That(buffer[itemOffset], Is.EqualTo((byte)ScreenOverlayItemKind.Text));
            Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(itemOffset + 53, 2)), Is.EqualTo(0));
            Assert.That(BinaryPrimitives.ReadInt32LittleEndian(buffer.Slice(itemOffset + 55, 4)), Is.EqualTo(1));
            Assert.That(buffer[itemOffset + 59], Is.EqualTo(1));
            Assert.That(buffer[itemOffset + 63], Is.EqualTo((byte)PresentationTextArgType.Int32));
            Assert.That(BinaryPrimitives.ReadInt32LittleEndian(buffer.Slice(itemOffset + 67, 4)), Is.EqualTo(7));

            int cursor = itemOffset + WireScreenOverlayItem.SizeInBytes;
            int stringCount = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(cursor, 2));
            Assert.That(stringCount, Is.EqualTo(1));
            cursor += 2;

            int rawStringLength = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(cursor, 2));
            cursor += 2 + rawStringLength;

            int templateCount = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(cursor, 2));
            Assert.That(templateCount, Is.EqualTo(1));
            cursor += 2;

            int tokenId = BinaryPrimitives.ReadInt32LittleEndian(buffer.Slice(cursor, 4));
            cursor += 4;
            int templateByteCount = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(cursor, 2));
            cursor += 2;
            string template = Encoding.UTF8.GetString(buffer.Slice(cursor, templateByteCount));

            Assert.That(tokenId, Is.EqualTo(1));
            Assert.That(template, Is.EqualTo("READY {0}"));
        }

        [Test]
        public void BinaryFrameEncoder_RoutesSurfaceItems_ToSurfaceSectionWithoutPrimitiveFallback()
        {
            var primitives = new PrimitiveDrawBuffer(4);
            MaterialCustomData surfaceData = MaterialCustomData.Empty.WithSlot(2, new Vector4(9f, 10f, 11f, 12f));
            Assert.That(primitives.TryAdd(CreatePrimitive(
                stableId: 101,
                assetKind: AssetKind.Mesh,
                renderPath: VisualRenderPath.InstancedStaticMesh,
                meshAssetId: 11,
                materialId: 21)), Is.True);
            Assert.That(primitives.TryAdd(CreatePrimitive(
                stableId: 202,
                assetKind: AssetKind.Surface,
                renderPath: VisualRenderPath.Surface,
                meshAssetId: 12,
                materialId: 22,
                surfaceLayerKey: "terrain.visual",
                sortId: 7,
                visibility: VisualVisibility.Culled,
                materialCustomData: surfaceData)), Is.True);

            var encoder = new BinaryFrameEncoder();
            var camera = new CameraRenderState3D(Vector3.Zero, Vector3.UnitZ, Vector3.UnitY, 60f);
            encoder.Encode(1, 2, 3, in camera, primitives, null, null, null, null, null, null, null);

            ReadOnlySpan<byte> buffer = encoder.GetResult();
            var (primitiveOffset, primitiveCount, primitiveBytes) = FindSection(buffer, FrameProtocol.SectionPrimitives);
            Assert.That(primitiveCount, Is.EqualTo(1));
            Assert.That(primitiveBytes, Is.EqualTo(WirePrimitiveDrawItem.SizeInBytes));
            Assert.That(BinaryPrimitives.ReadInt32LittleEndian(buffer.Slice(primitiveOffset, 4)), Is.EqualTo(11));

            var (surfaceOffset, surfaceCount, _) = FindSection(buffer, FrameProtocol.SectionSurfaces);
            Assert.That(surfaceCount, Is.EqualTo(1));
            Assert.That(BinaryPrimitives.ReadInt32LittleEndian(buffer.Slice(surfaceOffset, 4)), Is.EqualTo(202));
            Assert.That(BinaryPrimitives.ReadInt32LittleEndian(buffer.Slice(surfaceOffset + 4, 4)), Is.EqualTo(12));
            Assert.That(BinaryPrimitives.ReadInt32LittleEndian(buffer.Slice(surfaceOffset + 8, 4)), Is.EqualTo(22));
            Assert.That(BinaryPrimitives.ReadInt32LittleEndian(buffer.Slice(surfaceOffset + 12, 4)), Is.EqualTo(7));
            Assert.That(buffer[surfaceOffset + 56], Is.EqualTo((byte)VisualVisibility.Culled));
            Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(surfaceOffset + 57, 4)), Is.EqualTo(1u << 2));
            Assert.That(BinaryPrimitives.ReadSingleLittleEndian(buffer.Slice(surfaceOffset + 93, 4)), Is.EqualTo(9f));
            Assert.That(BinaryPrimitives.ReadSingleLittleEndian(buffer.Slice(surfaceOffset + 97, 4)), Is.EqualTo(10f));
            Assert.That(BinaryPrimitives.ReadSingleLittleEndian(buffer.Slice(surfaceOffset + 101, 4)), Is.EqualTo(11f));
            Assert.That(BinaryPrimitives.ReadSingleLittleEndian(buffer.Slice(surfaceOffset + 105, 4)), Is.EqualTo(12f));

            int layerLength = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(surfaceOffset + WireSurfaceDrawItem.FixedSizeInBytes, 2));
            string layerKey = Encoding.UTF8.GetString(buffer.Slice(surfaceOffset + WireSurfaceDrawItem.FixedSizeInBytes + 2, layerLength));
            Assert.That(layerKey, Is.EqualTo("terrain.visual"));
        }

        [Test]
        public void BinaryFrameEncoder_Throws_WhenSurfaceLaneContractIsViolated()
        {
            var primitives = new PrimitiveDrawBuffer(2);
            Assert.That(primitives.TryAdd(CreatePrimitive(
                stableId: 303,
                assetKind: AssetKind.Mesh,
                renderPath: VisualRenderPath.Surface,
                meshAssetId: 11,
                materialId: 21)), Is.True);

            var encoder = new BinaryFrameEncoder();
            var camera = new CameraRenderState3D(Vector3.Zero, Vector3.UnitZ, Vector3.UnitY, 60f);
            var ex = Assert.Throws<InvalidOperationException>(
                () => encoder.Encode(1, 2, 3, in camera, primitives, null, null, null, null, null, null, null));
            Assert.That(ex!.Message, Does.Contain("non-Surface"));
        }

        [Test]
        public void BinaryFrameEncoder_Throws_WhenPrimitiveLaneWouldDropMaterialCustomData()
        {
            var primitives = new PrimitiveDrawBuffer(2);
            MaterialCustomData customData = MaterialCustomData.Empty.WithSlot(0, new Vector4(1f, 2f, 3f, 4f));
            Assert.That(primitives.TryAdd(CreatePrimitive(
                stableId: 404,
                assetKind: AssetKind.Mesh,
                renderPath: VisualRenderPath.InstancedStaticMesh,
                meshAssetId: 11,
                materialId: 21,
                materialCustomData: customData)), Is.True);

            var encoder = new BinaryFrameEncoder();
            var camera = new CameraRenderState3D(Vector3.Zero, Vector3.UnitZ, Vector3.UnitY, 60f);
            var ex = Assert.Throws<InvalidOperationException>(
                () => encoder.Encode(1, 2, 3, in camera, primitives, null, null, null, null, null, null, null));
            Assert.That(ex!.Message, Does.Contain("MaterialCustomData"));
        }

        [Test]
        public void WebTransportLayer_MaterialMap_EncodesHostOwnedMaterialContract()
        {
            byte[] buffer = WebTransportLayer.EncodeMaterialMap(new[]
            {
                new WebMaterialMapEntry(
                    22,
                    "default_host_surface",
                    MaterialAssetDomain.Surface,
                    MaterialAssetFlags.SupportsPerInstanceCustomData,
                    new[] { "web.material:surface.per_instance_quad?baseColor=0.35,0.65,1.0" }),
            });

            Assert.That(buffer[0], Is.EqualTo(FrameProtocol.MsgTypeMaterialMap));
            Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(1, 2)), Is.EqualTo(1));

            int cursor = 3;
            Assert.That(BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(cursor, 4)), Is.EqualTo(22));
            cursor += 4;
            Assert.That(buffer[cursor++], Is.EqualTo((byte)MaterialAssetDomain.Surface));
            Assert.That(
                BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(cursor, 2)),
                Is.EqualTo((ushort)MaterialAssetFlags.SupportsPerInstanceCustomData));
            cursor += 2;

            string key = ReadUtf8(buffer, ref cursor);
            Assert.That(key, Is.EqualTo("default_host_surface"));
            Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(cursor, 2)), Is.EqualTo(1));
            cursor += 2;

            string uri = ReadUtf8(buffer, ref cursor);
            Assert.That(uri, Is.EqualTo("web.material:surface.per_instance_quad?baseColor=0.35,0.65,1.0"));
            Assert.That(cursor, Is.EqualTo(buffer.Length));
        }

        private static (int PayloadOffset, int ItemCount, int ByteLength) FindSection(ReadOnlySpan<byte> buffer, byte sectionType)
        {
            int cursor = FrameProtocol.FrameHeaderSize;
            while (cursor < buffer.Length)
            {
                byte currentSection = buffer[cursor];
                if (currentSection == FrameProtocol.SectionEnd)
                {
                    break;
                }

                int itemCount = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(cursor + 1, 2));
                int byteLength = BinaryPrimitives.ReadInt32LittleEndian(buffer.Slice(cursor + 3, 4));
                int payloadOffset = cursor + FrameProtocol.SectionHeaderSize;
                if (currentSection == sectionType)
                {
                    return (payloadOffset, itemCount, byteLength);
                }

                cursor = payloadOffset + byteLength;
            }

            throw new AssertionException($"Section 0x{sectionType:X2} was not found in the encoded frame.");
        }

        private static string ReadUtf8(byte[] buffer, ref int cursor)
        {
            int byteLength = BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(cursor, 2));
            cursor += 2;
            string value = Encoding.UTF8.GetString(buffer.AsSpan(cursor, byteLength));
            cursor += byteLength;
            return value;
        }

        private static WorldHudStringTable CreateWorldHudStrings(string templateSource)
        {
            var tokenIds = new StringIntRegistry(capacity: 4, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
            tokenIds.Register("hud.test");
            tokenIds.Freeze();

            var localeIds = new StringIntRegistry(capacity: 4, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
            localeIds.Register("en-US");
            localeIds.Freeze();

            var tokens = new PresentationTextTokenDefinition[2];
            tokens[1] = new PresentationTextTokenDefinition
            {
                TokenId = 1,
                Key = "hud.test",
                ArgCount = 2,
            };

            var templates = new PresentationTextTemplate[2];
            templates[1] = new PresentationTextTemplate(templateSource, Array.Empty<PresentationTextTemplatePart>());

            var locales = new PresentationTextLocaleTable[2];
            locales[1] = new PresentationTextLocaleTable(1, "en-US", templates);

            var catalog = new PresentationTextCatalog(tokenIds, tokens, localeIds, locales, defaultLocaleId: 1);
            var selection = new PresentationTextLocaleSelection(catalog);
            return new WorldHudStringTable(catalog, selection, dynamicCapacity: 4);
        }

        private static PrimitiveDrawItem CreatePrimitive(
            int stableId,
            AssetKind assetKind,
            VisualRenderPath renderPath,
            int meshAssetId,
            int materialId,
            string surfaceLayerKey = "",
            int sortId = 0,
            VisualVisibility visibility = VisualVisibility.Visible,
            MaterialCustomData materialCustomData = default)
        {
            return new PrimitiveDrawItem
            {
                AssetKind = assetKind,
                MeshAssetId = meshAssetId,
                Position = new Vector3(1f, 2f, 3f),
                Rotation = Quaternion.Identity,
                Scale = new Vector3(4f, 5f, 6f),
                Color = Vector4.One,
                StableId = stableId,
                MaterialId = materialId,
                TemplateId = stableId + 1000,
                RenderPath = renderPath,
                Mobility = VisualMobility.Static,
                Flags = VisualRuntimeFlags.Visible,
                Visibility = visibility,
                SurfaceLayerKey = surfaceLayerKey,
                SortId = sortId,
                MaterialCustomData = materialCustomData,
            };
        }
    }
}
