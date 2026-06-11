using System;
using System.Buffers.Binary;
using System.Text;
using Ludots.Adapter.Web.Protocol;
using Ludots.Core.Presentation.Camera;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Config;
using Ludots.Core.Presentation.DebugDraw;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Performers;
using Ludots.Core.Presentation.Rendering;

namespace Ludots.Adapter.Web.Streaming
{
    /// <summary>
    /// Zero-allocation binary encoder that serializes draw buffers into a contiguous byte span.
    /// Wire format: [FrameHeader] [Section]* [SectionEnd]
    /// </summary>
    public sealed class BinaryFrameEncoder
    {
        private byte[] _buffer;
        private int _pos;

        public BinaryFrameEncoder(int initialCapacity = 256 * 1024)
        {
            _buffer = new byte[initialCapacity];
        }

        public int EncodedLength { get; private set; }

        public int CopyTo(byte[] destination)
        {
            int len = EncodedLength;
            if (destination.Length < len)
                throw new ArgumentException($"Destination too small: {destination.Length} < {len}");
            Buffer.BlockCopy(_buffer, 0, destination, 0, len);
            return len;
        }

        public ReadOnlySpan<byte> GetResult() => new(_buffer, 0, EncodedLength);

        public void Encode(
            uint frameNumber,
            int simTick,
            long timestampMs,
            in CameraRenderState3D camera,
            PrimitiveDrawBuffer? primitives,
            GroundOverlayBuffer? groundOverlays,
            WorldHudBatchBuffer? worldHud,
            ScreenHudBatchBuffer? screenHud,
            WorldHudStringTable? worldHudStrings,
            DebugDrawCommandBuffer? debugDraw,
            ScreenOverlayBuffer? screenOverlay = null,
            string? uiSceneJson = null)
        {
            _pos = 0;
            EnsureCapacity(FrameProtocol.FrameHeaderSize);

            _buffer[_pos++] = FrameProtocol.MsgTypeFrame;
            WriteUInt32(frameNumber);
            WriteInt32(simTick);
            WriteInt64(timestampMs);

            WriteCamera(in camera);
            WritePrimitives(primitives);
            WriteSurfaces(primitives);
            WriteGroundOverlays(groundOverlays);
            WriteWorldHud(worldHud);
            WriteScreenHud(screenHud, worldHudStrings);
            WriteDebugDraw(debugDraw);
            WriteScreenOverlay(screenOverlay, worldHudStrings);
            WriteUiScene(uiSceneJson);

            EnsureCapacity(1);
            _buffer[_pos++] = FrameProtocol.SectionEnd;

            EncodedLength = _pos;
        }

        private void WriteCamera(in CameraRenderState3D cam)
        {
            int itemBytes = WireCameraState.SizeInBytes;
            WriteSectionHeader(FrameProtocol.SectionCamera, 1, itemBytes);
            EnsureCapacity(itemBytes);

            WriteFloat(cam.Position.X); WriteFloat(cam.Position.Y); WriteFloat(cam.Position.Z);
            WriteFloat(cam.Target.X); WriteFloat(cam.Target.Y); WriteFloat(cam.Target.Z);
            WriteFloat(cam.Up.X); WriteFloat(cam.Up.Y); WriteFloat(cam.Up.Z);
            WriteFloat(cam.FovYDeg);
        }

        private void WritePrimitives(PrimitiveDrawBuffer? buf)
        {
            if (buf == null || buf.Count == 0) return;

            var span = buf.GetSpan();
            int startPos = _pos;
            int count = 0;
            WriteSectionHeader(FrameProtocol.SectionPrimitives, 0, 0);

            for (int i = 0; i < span.Length; i++)
            {
                ref readonly var item = ref span[i];
                if (!IsPrimitiveLaneItem(in item))
                {
                    continue;
                }

                EnsureCapacity(WirePrimitiveDrawItem.SizeInBytes);
                WriteInt32(item.MeshAssetId);
                WriteFloat(item.Position.X); WriteFloat(item.Position.Y); WriteFloat(item.Position.Z);
                WriteFloat(item.Scale.X); WriteFloat(item.Scale.Y); WriteFloat(item.Scale.Z);
                WriteFloat(item.Color.X); WriteFloat(item.Color.Y); WriteFloat(item.Color.Z); WriteFloat(item.Color.W);
                count++;
            }

            if (count == 0)
            {
                _pos = startPos;
                return;
            }

            int itemBytes = count * WirePrimitiveDrawItem.SizeInBytes;
            BinaryPrimitives.WriteUInt16LittleEndian(_buffer.AsSpan(startPos + 1), checked((ushort)count));
            BinaryPrimitives.WriteInt32LittleEndian(_buffer.AsSpan(startPos + 3), itemBytes);
        }

        private void WriteSurfaces(PrimitiveDrawBuffer? buf)
        {
            if (buf == null || buf.Count == 0) return;

            var span = buf.GetSpan();
            int startPos = _pos;
            int count = 0;
            WriteSectionHeader(FrameProtocol.SectionSurfaces, 0, 0);

            for (int i = 0; i < span.Length; i++)
            {
                ref readonly var item = ref span[i];
                if (!IsSurfaceLaneItem(in item))
                {
                    continue;
                }

                WriteSurfaceItem(in item);
                count++;
            }

            if (count == 0)
            {
                _pos = startPos;
                return;
            }

            int itemBytes = _pos - startPos - FrameProtocol.SectionHeaderSize;
            BinaryPrimitives.WriteUInt16LittleEndian(_buffer.AsSpan(startPos + 1), checked((ushort)count));
            BinaryPrimitives.WriteInt32LittleEndian(_buffer.AsSpan(startPos + 3), itemBytes);
        }

        private void WriteGroundOverlays(GroundOverlayBuffer? buf)
        {
            if (buf == null || buf.Count == 0) return;

            var span = buf.GetSpan();
            int count = span.Length;
            int itemBytes = count * WireGroundOverlayItem.SizeInBytes;
            WriteSectionHeader(FrameProtocol.SectionGroundOverlays, (ushort)count, itemBytes);
            EnsureCapacity(itemBytes);

            for (int i = 0; i < count; i++)
            {
                ref readonly var item = ref span[i];
                _buffer[_pos++] = (byte)item.Shape;
                WriteFloat(item.Center.X); WriteFloat(item.Center.Y); WriteFloat(item.Center.Z);
                WriteFloat(item.Radius);
                WriteFloat(item.InnerRadius);
                WriteFloat(item.Angle);
                WriteFloat(item.Rotation);
                WriteFloat(item.Length);
                WriteFloat(item.Width);
                WriteFloat(item.FillColor.X); WriteFloat(item.FillColor.Y); WriteFloat(item.FillColor.Z); WriteFloat(item.FillColor.W);
                WriteFloat(item.BorderColor.X); WriteFloat(item.BorderColor.Y); WriteFloat(item.BorderColor.Z); WriteFloat(item.BorderColor.W);
                WriteFloat(item.BorderWidth);
            }
        }

        private void WriteWorldHud(WorldHudBatchBuffer? buf)
        {
            if (buf == null || buf.Count == 0) return;

            var span = buf.GetSpan();
            int count = span.Length;
            int itemBytes = count * WireWorldHudItem.SizeInBytes;
            WriteSectionHeader(FrameProtocol.SectionWorldHud, (ushort)count, itemBytes);
            EnsureCapacity(itemBytes);

            for (int i = 0; i < count; i++)
            {
                ref readonly var item = ref span[i];
                _buffer[_pos++] = (byte)item.Kind;
                WriteFloat(item.WorldPosition.X); WriteFloat(item.WorldPosition.Y); WriteFloat(item.WorldPosition.Z);
                WriteFloat(item.Color0.X); WriteFloat(item.Color0.Y); WriteFloat(item.Color0.Z); WriteFloat(item.Color0.W);
                WriteFloat(item.Color1.X); WriteFloat(item.Color1.Y); WriteFloat(item.Color1.Z); WriteFloat(item.Color1.W);
                WriteFloat(item.Width);
                WriteFloat(item.Height);
                WriteFloat(item.Value0);
                WriteFloat(item.Value1);
                WriteInt32(item.Id0);
                WriteInt32(item.Id1);
                WriteInt32(item.FontSize);
                WriteTextPacket(in item.Text);
            }
        }

        private void WriteScreenHud(ScreenHudBatchBuffer? buf, WorldHudStringTable? strings)
        {
            if (buf == null || buf.Count == 0) return;

            var span = buf.GetSpan();
            int count = span.Length;
            int startPos = _pos;
            WriteSectionHeader(FrameProtocol.SectionScreenHud, (ushort)count, 0);

            int maxStringId = 0;
            for (int i = 0; i < count; i++)
            {
                ref readonly var item = ref span[i];
                EnsureCapacity(WireWorldHudItem.SizeInBytes);
                _buffer[_pos++] = (byte)item.Kind;
                WriteFloat(item.ScreenX); WriteFloat(item.ScreenY); WriteFloat(0f);
                WriteFloat(item.Color0.X); WriteFloat(item.Color0.Y); WriteFloat(item.Color0.Z); WriteFloat(item.Color0.W);
                WriteFloat(item.Color1.X); WriteFloat(item.Color1.Y); WriteFloat(item.Color1.Z); WriteFloat(item.Color1.W);
                WriteFloat(item.Width);
                WriteFloat(item.Height);
                WriteFloat(item.Value0);
                WriteFloat(item.Value1);
                WriteInt32(item.Id0);
                WriteInt32(item.Id1);
                WriteInt32(item.FontSize);
                WriteTextPacket(in item.Text);
                if (item.Id0 > maxStringId)
                {
                    maxStringId = item.Id0;
                }
            }

            WriteScreenHudStringTable(maxStringId > 0 ? maxStringId + 1 : 0, strings);
            WriteTextTemplateTable(span, strings);

            int totalBytes = _pos - startPos - FrameProtocol.SectionHeaderSize;
            BinaryPrimitives.WriteInt32LittleEndian(_buffer.AsSpan(startPos + 3), totalBytes);
        }

        private void WriteDebugDraw(DebugDrawCommandBuffer? buf)
        {
            if (buf == null) return;

            if (buf.Lines.Count > 0)
            {
                int count = buf.Lines.Count;
                int itemBytes = count * WireDebugLine.SizeInBytes;
                WriteSectionHeader(FrameProtocol.SectionDebugLines, (ushort)Math.Min(count, ushort.MaxValue), itemBytes);
                EnsureCapacity(itemBytes);

                for (int i = 0; i < count; i++)
                {
                    var line = buf.Lines[i];
                    WriteFloat(line.A.X); WriteFloat(line.A.Y);
                    WriteFloat(line.B.X); WriteFloat(line.B.Y);
                    WriteFloat(line.Thickness);
                    _buffer[_pos++] = line.Color.R;
                    _buffer[_pos++] = line.Color.G;
                    _buffer[_pos++] = line.Color.B;
                    _buffer[_pos++] = line.Color.A;
                }
            }

            if (buf.Circles.Count > 0)
            {
                int count = buf.Circles.Count;
                int itemBytes = count * WireDebugCircle.SizeInBytes;
                WriteSectionHeader(FrameProtocol.SectionDebugCircles, (ushort)Math.Min(count, ushort.MaxValue), itemBytes);
                EnsureCapacity(itemBytes);

                for (int i = 0; i < count; i++)
                {
                    var circ = buf.Circles[i];
                    WriteFloat(circ.Center.X); WriteFloat(circ.Center.Y);
                    WriteFloat(circ.Radius);
                    WriteFloat(circ.Thickness);
                    _buffer[_pos++] = circ.Color.R;
                    _buffer[_pos++] = circ.Color.G;
                    _buffer[_pos++] = circ.Color.B;
                    _buffer[_pos++] = circ.Color.A;
                }
            }

            if (buf.Boxes.Count > 0)
            {
                int count = buf.Boxes.Count;
                int itemBytes = count * WireDebugBox.SizeInBytes;
                WriteSectionHeader(FrameProtocol.SectionDebugBoxes, (ushort)Math.Min(count, ushort.MaxValue), itemBytes);
                EnsureCapacity(itemBytes);

                for (int i = 0; i < count; i++)
                {
                    var box = buf.Boxes[i];
                    WriteFloat(box.Center.X); WriteFloat(box.Center.Y);
                    WriteFloat(box.HalfWidth);
                    WriteFloat(box.HalfHeight);
                    WriteFloat(box.RotationRadians);
                    WriteFloat(box.Thickness);
                    _buffer[_pos++] = box.Color.R;
                    _buffer[_pos++] = box.Color.G;
                    _buffer[_pos++] = box.Color.B;
                    _buffer[_pos++] = box.Color.A;
                }
            }
        }

        private void WriteScreenOverlay(ScreenOverlayBuffer? buf, WorldHudStringTable? strings)
        {
            if (buf == null || buf.Count == 0) return;

            var span = buf.GetSpan();
            int count = span.Length;

            int startPos = _pos;
            WriteSectionHeader(FrameProtocol.SectionScreenOverlay, (ushort)count, 0);

            for (int i = 0; i < count; i++)
            {
                ref readonly var item = ref span[i];
                EnsureCapacity(WireScreenOverlayItem.SizeInBytes);
                _buffer[_pos++] = (byte)item.Kind;
                WriteInt32(item.X);
                WriteInt32(item.Y);
                WriteInt32(item.Width);
                WriteInt32(item.Height);
                WriteInt32(item.FontSize);
                WriteFloat(item.Color.X); WriteFloat(item.Color.Y); WriteFloat(item.Color.Z); WriteFloat(item.Color.W);
                WriteFloat(item.BackgroundColor.X); WriteFloat(item.BackgroundColor.Y); WriteFloat(item.BackgroundColor.Z); WriteFloat(item.BackgroundColor.W);
                EnsureCapacity(2);
                BinaryPrimitives.WriteUInt16LittleEndian(_buffer.AsSpan(_pos), (ushort)item.StringId);
                _pos += 2;
                WriteTextPacket(in item.Text);
            }

            int stringCount = 0;
            for (int i = 0; i < count; i++)
            {
                ref readonly var item = ref span[i];
                if (item.Kind == ScreenOverlayItemKind.Text)
                    stringCount = Math.Max(stringCount, item.StringId + 1);
            }

            WriteOverlayStringTable(buf, stringCount);
            WriteTextTemplateTable(span, strings);

            int totalBytes = _pos - startPos - FrameProtocol.SectionHeaderSize;
            BinaryPrimitives.WriteInt32LittleEndian(_buffer.AsSpan(startPos + 3), totalBytes);
        }

        private void WriteTextPacket(in PresentationTextPacket packet)
        {
            EnsureCapacity(WirePresentationTextPacket.SizeInBytes);

            WriteInt32(packet.TokenId);
            _buffer[_pos++] = packet.ArgCount;
            _buffer[_pos++] = packet.Reserved0;
            BinaryPrimitives.WriteInt16LittleEndian(_buffer.AsSpan(_pos), packet.Reserved1);
            _pos += 2;

            WriteTextArg(in packet.Arg0);
            WriteTextArg(in packet.Arg1);
            WriteTextArg(in packet.Arg2);
            WriteTextArg(in packet.Arg3);
        }

        private void WriteTextArg(in PresentationTextArg arg)
        {
            EnsureCapacity(WirePresentationTextPacket.ArgSizeInBytes);
            _buffer[_pos++] = (byte)arg.Type;
            _buffer[_pos++] = (byte)arg.Format;
            BinaryPrimitives.WriteInt16LittleEndian(_buffer.AsSpan(_pos), arg.Reserved);
            _pos += 2;
            WriteInt32(arg.Raw32);
        }

        private void WriteScreenHudStringTable(int stringCount, WorldHudStringTable? strings)
        {
            EnsureCapacity(2);
            BinaryPrimitives.WriteUInt16LittleEndian(_buffer.AsSpan(_pos), (ushort)stringCount);
            _pos += 2;

            for (int i = 0; i < stringCount; i++)
            {
                string? text = strings?.TryGet(i);
                if (text == null) text = string.Empty;
                int byteCount = Encoding.UTF8.GetByteCount(text);
                EnsureCapacity(2 + byteCount);
                BinaryPrimitives.WriteUInt16LittleEndian(_buffer.AsSpan(_pos), (ushort)byteCount);
                _pos += 2;
                Encoding.UTF8.GetBytes(text, _buffer.AsSpan(_pos));
                _pos += byteCount;
            }
        }

        private void WriteOverlayStringTable(ScreenOverlayBuffer buffer, int stringCount)
        {
            EnsureCapacity(2);
            BinaryPrimitives.WriteUInt16LittleEndian(_buffer.AsSpan(_pos), (ushort)stringCount);
            _pos += 2;

            for (int i = 0; i < stringCount; i++)
            {
                string? s = buffer.GetString(i);
                if (s == null) s = string.Empty;
                int byteCount = Encoding.UTF8.GetByteCount(s);
                EnsureCapacity(2 + byteCount);
                BinaryPrimitives.WriteUInt16LittleEndian(_buffer.AsSpan(_pos), (ushort)byteCount);
                _pos += 2;
                Encoding.UTF8.GetBytes(s, _buffer.AsSpan(_pos));
                _pos += byteCount;
            }
        }

        private void WriteTextTemplateTable(ReadOnlySpan<ScreenHudItem> items, WorldHudStringTable? strings)
        {
            Span<int> tokenIds = items.Length <= 128 ? stackalloc int[items.Length] : new int[items.Length];
            int tokenCount = CollectUniqueTokenIds(items, strings, tokenIds);

            EnsureCapacity(2);
            BinaryPrimitives.WriteUInt16LittleEndian(_buffer.AsSpan(_pos), (ushort)tokenCount);
            _pos += 2;

            for (int i = 0; i < tokenCount; i++)
            {
                int tokenId = tokenIds[i];
                string template = strings!.TryGet(tokenId) ?? string.Empty;
                int byteCount = Encoding.UTF8.GetByteCount(template);
                EnsureCapacity(6 + byteCount);
                WriteInt32(tokenId);
                BinaryPrimitives.WriteUInt16LittleEndian(_buffer.AsSpan(_pos), (ushort)byteCount);
                _pos += 2;
                Encoding.UTF8.GetBytes(template, _buffer.AsSpan(_pos));
                _pos += byteCount;
            }
        }

        private void WriteTextTemplateTable(ReadOnlySpan<ScreenOverlayItem> items, WorldHudStringTable? strings)
        {
            Span<int> tokenIds = items.Length <= 128 ? stackalloc int[items.Length] : new int[items.Length];
            int tokenCount = CollectUniqueTokenIds(items, strings, tokenIds);

            EnsureCapacity(2);
            BinaryPrimitives.WriteUInt16LittleEndian(_buffer.AsSpan(_pos), (ushort)tokenCount);
            _pos += 2;

            for (int i = 0; i < tokenCount; i++)
            {
                int tokenId = tokenIds[i];
                string template = strings!.TryGet(tokenId) ?? string.Empty;
                int byteCount = Encoding.UTF8.GetByteCount(template);
                EnsureCapacity(6 + byteCount);
                WriteInt32(tokenId);
                BinaryPrimitives.WriteUInt16LittleEndian(_buffer.AsSpan(_pos), (ushort)byteCount);
                _pos += 2;
                Encoding.UTF8.GetBytes(template, _buffer.AsSpan(_pos));
                _pos += byteCount;
            }
        }

        private static int CollectUniqueTokenIds(ReadOnlySpan<ScreenHudItem> items, WorldHudStringTable? strings, Span<int> tokenIds)
        {
            int tokenCount = 0;
            for (int i = 0; i < items.Length; i++)
            {
                int tokenId = items[i].Text.TokenId;
                if (tokenId <= 0 || strings?.TryGet(tokenId) == null)
                {
                    continue;
                }

                bool exists = false;
                for (int tokenIndex = 0; tokenIndex < tokenCount; tokenIndex++)
                {
                    if (tokenIds[tokenIndex] == tokenId)
                    {
                        exists = true;
                        break;
                    }
                }

                if (!exists)
                {
                    tokenIds[tokenCount++] = tokenId;
                }
            }

            return tokenCount;
        }

        private static int CollectUniqueTokenIds(ReadOnlySpan<ScreenOverlayItem> items, WorldHudStringTable? strings, Span<int> tokenIds)
        {
            int tokenCount = 0;
            for (int i = 0; i < items.Length; i++)
            {
                int tokenId = items[i].Text.TokenId;
                if (tokenId <= 0 || strings?.TryGet(tokenId) == null)
                {
                    continue;
                }

                bool exists = false;
                for (int tokenIndex = 0; tokenIndex < tokenCount; tokenIndex++)
                {
                    if (tokenIds[tokenIndex] == tokenId)
                    {
                        exists = true;
                        break;
                    }
                }

                if (!exists)
                {
                    tokenIds[tokenCount++] = tokenId;
                }
            }

            return tokenCount;
        }

        private void WriteUiScene(string? sceneJson)
        {
            if (sceneJson == null) return;

            int startPos = _pos;
            WriteSectionHeader(FrameProtocol.SectionUiScene, 1, 0);

            byte[] sceneBytes = Encoding.UTF8.GetBytes(sceneJson);

            EnsureCapacity(4 + sceneBytes.Length);
            WriteInt32(sceneBytes.Length);
            if (sceneBytes.Length > 0)
            {
                sceneBytes.CopyTo(_buffer.AsSpan(_pos));
                _pos += sceneBytes.Length;
            }

            int totalBytes = _pos - startPos - FrameProtocol.SectionHeaderSize;
            BinaryPrimitives.WriteInt32LittleEndian(_buffer.AsSpan(startPos + 3), totalBytes);
        }

        private void WriteSectionHeader(byte sectionType, ushort itemCount, int byteLength)
        {
            EnsureCapacity(FrameProtocol.SectionHeaderSize);
            _buffer[_pos++] = sectionType;
            BinaryPrimitives.WriteUInt16LittleEndian(_buffer.AsSpan(_pos), itemCount);
            _pos += 2;
            BinaryPrimitives.WriteInt32LittleEndian(_buffer.AsSpan(_pos), byteLength);
            _pos += 4;
        }

        private void WriteSurfaceItem(in PrimitiveDrawItem item)
        {
            int layerKeyByteCount = Encoding.UTF8.GetByteCount(item.SurfaceLayerKey);
            EnsureCapacity(WireSurfaceDrawItem.FixedSizeInBytes + 2 + layerKeyByteCount);

            WriteInt32(item.StableId);
            WriteInt32(item.MeshAssetId);
            WriteInt32(item.MaterialId);
            WriteInt32(item.SortId);
            WriteFloat(item.Position.X); WriteFloat(item.Position.Y); WriteFloat(item.Position.Z);
            WriteFloat(item.Rotation.X); WriteFloat(item.Rotation.Y); WriteFloat(item.Rotation.Z); WriteFloat(item.Rotation.W);
            WriteFloat(item.Scale.X); WriteFloat(item.Scale.Y); WriteFloat(item.Scale.Z);
            _buffer[_pos++] = (byte)item.Visibility;
            WriteMaterialCustomData(in item.MaterialCustomData);
            WriteUtf8ShortString(item.SurfaceLayerKey, layerKeyByteCount);
        }

        private void WriteMaterialCustomData(in MaterialCustomData data)
        {
            WriteUInt32(data.SlotMask);
            WriteVector4(data.Slot0);
            WriteVector4(data.Slot1);
            WriteVector4(data.Slot2);
            WriteVector4(data.Slot3);
        }

        private void WriteVector4(in System.Numerics.Vector4 value)
        {
            WriteFloat(value.X);
            WriteFloat(value.Y);
            WriteFloat(value.Z);
            WriteFloat(value.W);
        }

        private void WriteUtf8ShortString(string value, int byteCount)
        {
            if (byteCount > ushort.MaxValue)
            {
                throw new InvalidOperationException(
                    $"Surface layer key exceeds {ushort.MaxValue} UTF-8 bytes.");
            }

            EnsureCapacity(2 + byteCount);
            BinaryPrimitives.WriteUInt16LittleEndian(_buffer.AsSpan(_pos), (ushort)byteCount);
            _pos += 2;
            Encoding.UTF8.GetBytes(value, _buffer.AsSpan(_pos));
            _pos += byteCount;
        }

        private static bool IsPrimitiveLaneItem(in PrimitiveDrawItem item)
        {
            ValidateLaneRouting(in item);
            return item.AssetKind != AssetKind.Surface;
        }

        private static bool IsSurfaceLaneItem(in PrimitiveDrawItem item)
        {
            ValidateLaneRouting(in item);
            return item.AssetKind == AssetKind.Surface;
        }

        private static void ValidateLaneRouting(in PrimitiveDrawItem item)
        {
            if (item.AssetKind == AssetKind.Surface)
            {
                if (item.RenderPath != VisualRenderPath.Surface)
                {
                    throw new InvalidOperationException(
                        $"BinaryFrameEncoder received Surface visual stableId={item.StableId} on render path '{item.RenderPath}'.");
                }

                if (string.IsNullOrWhiteSpace(item.SurfaceLayerKey))
                {
                    throw new InvalidOperationException(
                        $"BinaryFrameEncoder received Surface visual stableId={item.StableId} without SurfaceLayerKey.");
                }

                return;
            }

            if (item.RenderPath == VisualRenderPath.Surface)
            {
                throw new InvalidOperationException(
                    $"BinaryFrameEncoder received non-Surface assetKind '{item.AssetKind}' on Surface render path.");
            }

            if (item.MaterialCustomData.HasAny)
            {
                throw new InvalidOperationException(
                    $"BinaryFrameEncoder cannot consume MaterialCustomData for non-Surface stableId={item.StableId}. Configure a Web lane that binds per-instance custom data.");
            }
        }

        private void WriteFloat(float value)
        {
            BinaryPrimitives.WriteSingleLittleEndian(_buffer.AsSpan(_pos), value);
            _pos += 4;
        }

        private void WriteInt32(int value)
        {
            BinaryPrimitives.WriteInt32LittleEndian(_buffer.AsSpan(_pos), value);
            _pos += 4;
        }

        private void WriteUInt32(uint value)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(_buffer.AsSpan(_pos), value);
            _pos += 4;
        }

        private void WriteInt64(long value)
        {
            BinaryPrimitives.WriteInt64LittleEndian(_buffer.AsSpan(_pos), value);
            _pos += 8;
        }

        private void EnsureCapacity(int additionalBytes)
        {
            int required = _pos + additionalBytes;
            if (required <= _buffer.Length) return;
            int newSize = Math.Max(_buffer.Length * 2, required);
            Array.Resize(ref _buffer, newSize);
        }
    }
}
