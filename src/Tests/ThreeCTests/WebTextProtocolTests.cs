using System;
using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using Ludots.Adapter.Web.Protocol;
using Ludots.Adapter.Web.Streaming;
using Ludots.Core.Presentation;
using Ludots.Core.Presentation.Camera;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Config;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Minimap;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Registry;
using NUnit.Framework;

namespace Ludots.Tests.ThreeC
{
    [TestFixture]
    public sealed class WebTextProtocolTests
    {
        [Test]
        public void BinaryFrameEncoder_SkinnedVisualsShareTheWebPrimitiveTransformLane()
        {
            var primitives = new PrimitiveDrawBuffer(capacity: 2);
            Assert.That(primitives.TryAdd(new PrimitiveDrawItem
            {
                MeshAssetId = 11,
                Position = Vector3.Zero,
                Scale = Vector3.One,
                Color = Vector4.One,
                StableId = 101,
                RenderPath = VisualRenderPath.StaticMesh,
            }), Is.True);
            Assert.That(primitives.TryAdd(new PrimitiveDrawItem
            {
                MeshAssetId = 77,
                Position = new Vector3(1f, 2f, 3f),
                Scale = new Vector3(4f, 5f, 6f),
                Color = new Vector4(0.1f, 0.2f, 0.3f, 1f),
                StableId = 202,
                RenderPath = VisualRenderPath.SkinnedMesh,
            }), Is.True);

            var skinned = new SkinnedVisualBatchBuffer(capacity: 1);
            Assert.That(skinned.TryAdd(new SkinnedVisualBatchItem
            {
                MeshAssetId = 77,
                Position = new Vector3(1f, 2f, 3f),
                Scale = new Vector3(4f, 5f, 6f),
                Color = new Vector4(0.1f, 0.2f, 0.3f, 1f),
                StableId = 202,
                Visibility = VisualVisibility.Visible,
            }), Is.True);

            var encoder = new BinaryFrameEncoder();
            var camera = new CameraRenderState3D(Vector3.Zero, Vector3.UnitZ, Vector3.UnitY, 60f);
            encoder.Encode(1, 2, 3, in camera, primitives, null, null, null, null, null, null, null, skinned);

            ReadOnlySpan<byte> buffer = encoder.GetResult();
            var (payloadOffset, itemCount, byteLength) = FindSection(buffer, FrameProtocol.SectionPrimitives);
            Assert.That(itemCount, Is.EqualTo(2), "The skinned projection must replace, not duplicate, its legacy primitive-lane entry.");
            Assert.That(byteLength, Is.EqualTo(2 * WirePrimitiveDrawItem.SizeInBytes));
            Assert.That(BinaryPrimitives.ReadInt32LittleEndian(buffer.Slice(payloadOffset, 4)), Is.EqualTo(11));
            Assert.That(BinaryPrimitives.ReadInt32LittleEndian(buffer.Slice(payloadOffset + 44, 4)), Is.EqualTo(101));
            int skinnedOffset = payloadOffset + WirePrimitiveDrawItem.SizeInBytes;
            Assert.That(BinaryPrimitives.ReadInt32LittleEndian(buffer.Slice(skinnedOffset, 4)), Is.EqualTo(77));
            Assert.That(BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(buffer.Slice(skinnedOffset + 4, 4))), Is.EqualTo(1f));
            Assert.That(BinaryPrimitives.ReadInt32LittleEndian(buffer.Slice(skinnedOffset + 44, 4)), Is.EqualTo(202));
        }

        [Test]
        public void BinaryFrameEncoder_FastPathSkinnedVisuals_ExcludeHiddenAndCulledSnapshots()
        {
            var skinned = new SkinnedVisualBatchBuffer(capacity: 3);
            Assert.That(skinned.TryAdd(new SkinnedVisualBatchItem
            {
                MeshAssetId = 77,
                Scale = Vector3.One,
                Color = Vector4.One,
                StableId = 707,
                Visibility = VisualVisibility.Visible,
            }), Is.True);
            Assert.That(skinned.TryAdd(new SkinnedVisualBatchItem
            {
                MeshAssetId = 78,
                Scale = Vector3.One,
                Color = Vector4.One,
                Visibility = VisualVisibility.Hidden,
            }), Is.True);
            Assert.That(skinned.TryAdd(new SkinnedVisualBatchItem
            {
                MeshAssetId = 79,
                Scale = Vector3.One,
                Color = Vector4.One,
                Visibility = VisualVisibility.Culled,
            }), Is.True);

            var encoder = new BinaryFrameEncoder();
            var camera = new CameraRenderState3D(Vector3.Zero, Vector3.UnitZ, Vector3.UnitY, 60f);
            encoder.Encode(1, 2, 3, in camera, null, null, null, null, null, null, null, null, skinned);

            ReadOnlySpan<byte> buffer = encoder.GetResult();
            var (payloadOffset, itemCount, byteLength) = FindSection(buffer, FrameProtocol.SectionPrimitives);
            Assert.That(itemCount, Is.EqualTo(1));
            Assert.That(byteLength, Is.EqualTo(WirePrimitiveDrawItem.SizeInBytes));
            Assert.That(BinaryPrimitives.ReadInt32LittleEndian(buffer.Slice(payloadOffset, 4)), Is.EqualTo(77));
            Assert.That(BinaryPrimitives.ReadInt32LittleEndian(buffer.Slice(payloadOffset + 44, 4)), Is.EqualTo(707));
        }

        [Test]
        public void BinaryFrameEncoder_StaticAndSkinnedVisualCountBeyondWireCapacity_ThrowsExplicitly()
        {
            var primitives = new PrimitiveDrawBuffer(capacity: ushort.MaxValue);
            var primitive = new PrimitiveDrawItem
            {
                MeshAssetId = 11,
                Scale = Vector3.One,
                Color = Vector4.One,
                RenderPath = VisualRenderPath.StaticMesh,
            };
            for (int i = 0; i < ushort.MaxValue; i++)
            {
                if (!primitives.TryAdd(primitive))
                {
                    throw new InvalidOperationException($"Primitive test fixture overflowed at {i}.");
                }
            }

            var skinned = new SkinnedVisualBatchBuffer(capacity: 1);
            Assert.That(skinned.TryAdd(new SkinnedVisualBatchItem
            {
                MeshAssetId = 77,
                Scale = Vector3.One,
                Color = Vector4.One,
                Visibility = VisualVisibility.Visible,
            }), Is.True);

            var encoder = new BinaryFrameEncoder();
            var camera = new CameraRenderState3D(Vector3.Zero, Vector3.UnitZ, Vector3.UnitY, 60f);
            InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
                encoder.Encode(1, 2, 3, in camera, primitives, null, null, null, null, null, null, null, skinned))!;
            Assert.That(error.Message, Does.Contain(ushort.MaxValue.ToString()));
        }

        [Test]
        public void BinaryFrameEncoder_OmitsRedundantWorldHudLane()
        {
            var worldHud = new WorldHudBatchBuffer(2);
            Assert.That(worldHud.TryAdd(new WorldHudItem
            {
                Kind = WorldHudItemKind.Bar,
                WorldPosition = new Vector3(2f, 3f, 4f),
                Width = 24f,
                Height = 4f,
                Value0 = 0.75f,
            }), Is.True);

            var screenHud = new ScreenHudBatchBuffer(2);
            Assert.That(screenHud.TryAdd(new ScreenHudItem
            {
                Kind = WorldHudItemKind.Bar,
                ScreenX = 100f,
                ScreenY = 80f,
                Width = 24f,
                Height = 4f,
                Value0 = 0.75f,
            }), Is.True);

            var encoder = new BinaryFrameEncoder();
            var camera = new CameraRenderState3D(Vector3.Zero, Vector3.UnitZ, Vector3.UnitY, 60f);
            encoder.Encode(1, 2, 3, in camera, null, null, worldHud, screenHud, null, null);

            ReadOnlySpan<byte> buffer = encoder.GetResult();
            Assert.That(ContainsSection(buffer, FrameProtocol.SectionWorldHud), Is.False);
            Assert.That(ContainsSection(buffer, FrameProtocol.SectionScreenHud), Is.True);
        }

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
                StableId = 333,
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
            Assert.That(BinaryPrimitives.ReadInt32LittleEndian(buffer.Slice(itemOffset + 9, 4)), Is.EqualTo(333));
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
        public void BinaryFrameEncoder_ScreenOverlay_EncodesLineThicknessAndClipShape()
        {
            var overlay = new ScreenOverlayBuffer();
            PresentationClipShape clip = PresentationClipShape.FromCircle(10f, 20f, 300f, 300f);
            Assert.That(overlay.AddLine(12, 24, 112, 224, 3, Vector4.One, 7, 9, clip), Is.True);

            var encoder = new BinaryFrameEncoder();
            var camera = new CameraRenderState3D(Vector3.Zero, Vector3.UnitZ, Vector3.UnitY, 60f);
            encoder.Encode(1, 2, 3, in camera, null, null, null, null, null, null, overlay);

            ReadOnlySpan<byte> buffer = encoder.GetResult();
            var (payloadOffset, itemCount, byteLength) = FindSection(buffer, FrameProtocol.SectionScreenOverlay);
            Assert.That(itemCount, Is.EqualTo(1));
            Assert.That(byteLength, Is.GreaterThanOrEqualTo(WireScreenOverlayItem.SizeInBytes));
            Assert.That(buffer[payloadOffset], Is.EqualTo((byte)ScreenOverlayItemKind.Line));
            Assert.That(BinaryPrimitives.ReadInt32LittleEndian(buffer.Slice(payloadOffset + 95, 4)), Is.EqualTo(3));
            Assert.That(buffer[payloadOffset + 99], Is.EqualTo((byte)PresentationClipShapeKind.Circle));
            Assert.That(BinaryPrimitives.ReadSingleLittleEndian(buffer.Slice(payloadOffset + 100, 4)), Is.EqualTo(10f));
            Assert.That(BinaryPrimitives.ReadSingleLittleEndian(buffer.Slice(payloadOffset + 104, 4)), Is.EqualTo(20f));
            Assert.That(BinaryPrimitives.ReadSingleLittleEndian(buffer.Slice(payloadOffset + 108, 4)), Is.EqualTo(300f));
            Assert.That(BinaryPrimitives.ReadSingleLittleEndian(buffer.Slice(payloadOffset + 112, 4)), Is.EqualTo(300f));
        }

        [Test]
        public void BinaryFrameEncoder_MinimapMarkers_EncodesClipStyleAndOrientation()
        {
            var markers = new MinimapScreenMarkerBuffer(capacity: 2);
            markers.BeginFrame();
            markers.SetClipShape(PresentationClipShape.FromDiamond(100f, 50f, 240f, 240f));
            var color = new Vector4(0.2f, 0.7f, 0.4f, 0.9f);
            Assert.That(markers.TryAdd(
                stableId: 17,
                screenX: 180f,
                screenY: 90f,
                in color,
                sizePx: 6f,
                flags: MinimapMarkerFlags.HasOrientation,
                orientationRad: 1.25f,
                orientationLengthPx: 9f), Is.True);

            var encoder = new BinaryFrameEncoder();
            var camera = new CameraRenderState3D(Vector3.Zero, Vector3.UnitZ, Vector3.UnitY, 60f);
            encoder.Encode(
                1,
                2,
                3,
                in camera,
                null,
                null,
                null,
                null,
                null,
                null,
                minimapMarkers: markers);

            ReadOnlySpan<byte> buffer = encoder.GetResult();
            var (payloadOffset, itemCount, byteLength) = FindSection(buffer, FrameProtocol.SectionMinimapMarkers);
            Assert.That(itemCount, Is.EqualTo(1));
            Assert.That(byteLength, Is.EqualTo(WireMinimapMarkers.MetadataSizeInBytes + WireMinimapMarkers.ItemSizeInBytes));
            Assert.That(buffer[payloadOffset], Is.EqualTo((byte)PresentationClipShapeKind.Diamond));
            Assert.That(BinaryPrimitives.ReadSingleLittleEndian(buffer.Slice(payloadOffset + 1, 4)), Is.EqualTo(100f));

            int markerOffset = payloadOffset + WireMinimapMarkers.MetadataSizeInBytes;
            Assert.That(BinaryPrimitives.ReadSingleLittleEndian(buffer.Slice(markerOffset, 4)), Is.EqualTo(180f));
            Assert.That(BinaryPrimitives.ReadSingleLittleEndian(buffer.Slice(markerOffset + 4, 4)), Is.EqualTo(90f));
            Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(markerOffset + 8, 4)),
                Is.EqualTo(MinimapScreenMarkerBuffer.PackColorKey(color)));
            Assert.That(BinaryPrimitives.ReadSingleLittleEndian(buffer.Slice(markerOffset + 12, 4)), Is.EqualTo(6f));
            Assert.That(BinaryPrimitives.ReadSingleLittleEndian(buffer.Slice(markerOffset + 20, 4)), Is.GreaterThan(0f));
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

        private static bool ContainsSection(ReadOnlySpan<byte> buffer, byte sectionType)
        {
            int cursor = FrameProtocol.FrameHeaderSize;
            while (cursor < buffer.Length)
            {
                byte currentSection = buffer[cursor];
                if (currentSection == FrameProtocol.SectionEnd)
                {
                    return false;
                }

                int byteLength = BinaryPrimitives.ReadInt32LittleEndian(buffer.Slice(cursor + 3, 4));
                if (currentSection == sectionType)
                {
                    return true;
                }

                cursor += FrameProtocol.SectionHeaderSize + byteLength;
            }

            return false;
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
            return new WorldHudStringTable(catalog, selection, runtimeStringCapacity: 4);
        }
    }
}
