using System;
using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using Ludots.Adapter.Web.Protocol;
using Ludots.Adapter.Web.Streaming;
using Ludots.Core.Presentation.Camera;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Config;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Registry;
using NUnit.Framework;
using Ludots.Platform.Abstractions;

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
                RenderPath = VisualRenderPath.StaticMesh,
            }), Is.True);
            Assert.That(primitives.TryAdd(new PrimitiveDrawItem
            {
                MeshAssetId = 77,
                Position = new Vector3(1f, 2f, 3f),
                Scale = new Vector3(4f, 5f, 6f),
                Color = new Vector4(0.1f, 0.2f, 0.3f, 1f),
                RenderPath = VisualRenderPath.SkinnedMesh,
            }), Is.True);

            var skinned = new SkinnedVisualBatchBuffer(capacity: 1);
            Assert.That(skinned.TryAdd(new SkinnedVisualBatchItem
            {
                MeshAssetId = 77,
                Position = new Vector3(1f, 2f, 3f),
                Scale = new Vector3(4f, 5f, 6f),
                Color = new Vector4(0.1f, 0.2f, 0.3f, 1f),
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
            int skinnedOffset = payloadOffset + WirePrimitiveDrawItem.SizeInBytes;
            Assert.That(BinaryPrimitives.ReadInt32LittleEndian(buffer.Slice(skinnedOffset, 4)), Is.EqualTo(77));
            Assert.That(BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(buffer.Slice(skinnedOffset + 4, 4))), Is.EqualTo(1f));
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
