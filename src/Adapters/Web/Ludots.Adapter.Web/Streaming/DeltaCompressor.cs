using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;
using Ludots.Adapter.Web.Protocol;
using Ludots.Core.Presentation.Camera;
using Ludots.Core.Presentation.Config;
using Ludots.Core.Presentation.DebugDraw;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Rendering;

namespace Ludots.Adapter.Web.Streaming
{
    /// <summary>
    /// Delta encoder: compares current frame against previous and emits only changed items.
    /// Falls back to full frame if delta would exceed the full size.
    /// </summary>
    public sealed class DeltaCompressor
    {
        private PrimitiveDrawItem[] _prevPrimitives = Array.Empty<PrimitiveDrawItem>();
        private int _prevPrimitiveCount;
        private readonly Dictionary<int, int> _prevPrimitiveIndices = new();
        private readonly Dictionary<int, int> _currentPrimitiveIndices = new();
        private bool _prevPrimitivesHaveStableIdentity = true;
        private int _prevDebugLineHash;
        private int _prevDebugCircleHash;
        private int _prevDebugBoxHash;
        private int _prevGroundOverlayHash;

        private byte[] _buffer = new byte[128 * 1024];
        private int _pos;

        public int EncodedLength { get; private set; }

        public int CopyTo(byte[] destination)
        {
            int len = EncodedLength;
            if (destination.Length < len)
                throw new ArgumentException($"Destination too small: {destination.Length} < {len}");
            Buffer.BlockCopy(_buffer, 0, destination, 0, len);
            return len;
        }

        public bool TryEncodeDelta(
            uint frameNumber,
            int simTick,
            long timestampMs,
            in CameraRenderState3D camera,
            PrimitiveDrawBuffer? primitives,
            GroundOverlayBuffer? groundOverlays,
            ScreenHudBatchBuffer? screenHud,
            WorldHudStringTable? worldHudStrings,
            DebugDrawCommandBuffer? debugDraw,
            ScreenOverlayBuffer? screenOverlay = null,
            string? uiSceneJson = null)
        {
            _pos = 0;
            EnsureCapacity(FrameProtocol.FrameHeaderSize);

            _buffer[_pos++] = FrameProtocol.MsgTypeDelta;
            WriteUInt32(frameNumber);
            WriteInt32(simTick);
            WriteInt64(timestampMs);

            WriteCameraSection(in camera);
            WritePrimitiveDelta(primitives);
            WriteGroundOverlayIfChanged(groundOverlays);
            WriteScreenHud(screenHud, worldHudStrings);
            WriteDebugDrawIfChanged(debugDraw);
            WriteScreenOverlay(screenOverlay);
            WriteUiScene(uiSceneJson);

            EnsureCapacity(1);
            _buffer[_pos++] = FrameProtocol.SectionEnd;
            EncodedLength = _pos;

            SnapshotPrimitives(primitives);
            return true;
        }

        private void WriteCameraSection(in CameraRenderState3D cam)
        {
            int itemBytes = WireCameraState.SizeInBytes;
            WriteSectionHeader(FrameProtocol.SectionCamera, 1, itemBytes);
            EnsureCapacity(itemBytes);
            WriteFloat(cam.Position.X); WriteFloat(cam.Position.Y); WriteFloat(cam.Position.Z);
            WriteFloat(cam.Target.X); WriteFloat(cam.Target.Y); WriteFloat(cam.Target.Z);
            WriteFloat(cam.Up.X); WriteFloat(cam.Up.Y); WriteFloat(cam.Up.Z);
            WriteFloat(cam.FovYDeg);
        }

        private int WritePrimitiveDelta(PrimitiveDrawBuffer? buf)
        {
            int curCount = buf?.Count ?? 0;
            ReadOnlySpan<PrimitiveDrawItem> curSpan = buf != null ? buf.GetSpan() : ReadOnlySpan<PrimitiveDrawItem>.Empty;

            if (!TryIndexPrimitivesByStableId(curSpan, curCount, _currentPrimitiveIndices) ||
                (_prevPrimitiveCount > 0 && !_prevPrimitivesHaveStableIdentity))
            {
                WritePrimitives(buf);
                return curCount;
            }

            int startPos = _pos;
            WriteSectionHeader(FrameProtocol.SectionPrimitivesDelta, 0, 0);
            EnsureCapacity(4);
            BinaryPrimitives.WriteUInt16LittleEndian(_buffer.AsSpan(_pos), (ushort)curCount);
            _pos += 2;
            int removedCountOffset = _pos;
            _pos += 2;

            int removedCount = 0;
            foreach (var kvp in _prevPrimitiveIndices)
            {
                if (_currentPrimitiveIndices.ContainsKey(kvp.Key))
                {
                    continue;
                }

                EnsureCapacity(4);
                WriteInt32(kvp.Key);
                removedCount++;
            }

            int changedCount = 0;
            for (int i = 0; i < curCount; i++)
            {
                ref readonly var item = ref curSpan[i];
                bool changed = !_prevPrimitiveIndices.TryGetValue(item.StableId, out int prevIndex) ||
                    !WireEquals(in item, in _prevPrimitives[prevIndex]);

                if (changed)
                {
                    EnsureCapacity(WirePrimitiveDrawItem.SizeInBytes);
                    WritePrimitiveItem(in item);
                    changedCount++;
                }
            }

            EnsureCapacity(curCount * sizeof(int));
            for (int i = 0; i < curCount; i++)
            {
                WriteInt32(curSpan[i].StableId);
            }

            int totalBytes = _pos - startPos - FrameProtocol.SectionHeaderSize;
            BinaryPrimitives.WriteUInt16LittleEndian(_buffer.AsSpan(removedCountOffset), (ushort)removedCount);
            _buffer[startPos] = FrameProtocol.SectionPrimitivesDelta;
            BinaryPrimitives.WriteUInt16LittleEndian(_buffer.AsSpan(startPos + 1), (ushort)changedCount);
            BinaryPrimitives.WriteInt32LittleEndian(_buffer.AsSpan(startPos + 3), totalBytes);
            return changedCount;
        }

        private void WritePrimitives(PrimitiveDrawBuffer? buf)
        {
            ReadOnlySpan<PrimitiveDrawItem> span = buf != null
                ? buf.GetSpan()
                : ReadOnlySpan<PrimitiveDrawItem>.Empty;
            int count = span.Length;
            int itemBytes = count * WirePrimitiveDrawItem.SizeInBytes;
            WriteSectionHeader(FrameProtocol.SectionPrimitives, (ushort)count, itemBytes);
            EnsureCapacity(itemBytes);

            for (int i = 0; i < count; i++)
            {
                ref readonly var item = ref span[i];
                WritePrimitiveItem(in item);
            }
        }

        private void WriteGroundOverlayIfChanged(GroundOverlayBuffer? buf)
        {
            int hash = HashBuffer(buf);
            if (hash == _prevGroundOverlayHash) return;
            _prevGroundOverlayHash = hash;

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
                WriteFloat(item.Radius); WriteFloat(item.InnerRadius); WriteFloat(item.Angle);
                WriteFloat(item.Rotation); WriteFloat(item.Length); WriteFloat(item.Width);
                WriteFloat(item.FillColor.X); WriteFloat(item.FillColor.Y); WriteFloat(item.FillColor.Z); WriteFloat(item.FillColor.W);
                WriteFloat(item.BorderColor.X); WriteFloat(item.BorderColor.Y); WriteFloat(item.BorderColor.Z); WriteFloat(item.BorderColor.W);
                WriteFloat(item.BorderWidth);
            }
        }

        private void WriteScreenHud(ScreenHudBatchBuffer? buf, WorldHudStringTable? strings)
        {
            ReadOnlySpan<ScreenHudItem> span = buf != null
                ? buf.GetSpan()
                : ReadOnlySpan<ScreenHudItem>.Empty;
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
                if (item.Id0 > maxStringId)
                {
                    maxStringId = item.Id0;
                }
            }

            int stringCount = maxStringId > 0 ? maxStringId + 1 : 0;
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

            int totalBytes = _pos - startPos - FrameProtocol.SectionHeaderSize;
            BinaryPrimitives.WriteInt32LittleEndian(_buffer.AsSpan(startPos + 3), totalBytes);
        }

        private void WriteDebugDrawIfChanged(DebugDrawCommandBuffer? buf)
        {
            if (buf == null) return;
            int lHash = buf.Lines.Count;
            int cHash = buf.Circles.Count;
            int bHash = buf.Boxes.Count;

            if (lHash != _prevDebugLineHash)
            {
                _prevDebugLineHash = lHash;
                if (buf.Lines.Count > 0)
                {
                    int count = buf.Lines.Count;
                    int itemBytes = count * WireDebugLine.SizeInBytes;
                    WriteSectionHeader(FrameProtocol.SectionDebugLines, (ushort)Math.Min(count, ushort.MaxValue), itemBytes);
                    EnsureCapacity(itemBytes);
                    for (int i = 0; i < count; i++)
                    {
                        var l = buf.Lines[i];
                        WriteFloat(l.A.X); WriteFloat(l.A.Y); WriteFloat(l.B.X); WriteFloat(l.B.Y);
                        WriteFloat(l.Thickness);
                        _buffer[_pos++] = l.Color.R; _buffer[_pos++] = l.Color.G;
                        _buffer[_pos++] = l.Color.B; _buffer[_pos++] = l.Color.A;
                    }
                }
            }

            if (cHash != _prevDebugCircleHash)
            {
                _prevDebugCircleHash = cHash;
                if (buf.Circles.Count > 0)
                {
                    int count = buf.Circles.Count;
                    int itemBytes = count * WireDebugCircle.SizeInBytes;
                    WriteSectionHeader(FrameProtocol.SectionDebugCircles, (ushort)Math.Min(count, ushort.MaxValue), itemBytes);
                    EnsureCapacity(itemBytes);
                    for (int i = 0; i < count; i++)
                    {
                        var c = buf.Circles[i];
                        WriteFloat(c.Center.X); WriteFloat(c.Center.Y);
                        WriteFloat(c.Radius); WriteFloat(c.Thickness);
                        _buffer[_pos++] = c.Color.R; _buffer[_pos++] = c.Color.G;
                        _buffer[_pos++] = c.Color.B; _buffer[_pos++] = c.Color.A;
                    }
                }
            }

            if (bHash != _prevDebugBoxHash)
            {
                _prevDebugBoxHash = bHash;
                if (buf.Boxes.Count > 0)
                {
                    int count = buf.Boxes.Count;
                    int itemBytes = count * WireDebugBox.SizeInBytes;
                    WriteSectionHeader(FrameProtocol.SectionDebugBoxes, (ushort)Math.Min(count, ushort.MaxValue), itemBytes);
                    EnsureCapacity(itemBytes);
                    for (int i = 0; i < count; i++)
                    {
                        var b = buf.Boxes[i];
                        WriteFloat(b.Center.X); WriteFloat(b.Center.Y);
                        WriteFloat(b.HalfWidth); WriteFloat(b.HalfHeight);
                        WriteFloat(b.RotationRadians); WriteFloat(b.Thickness);
                        _buffer[_pos++] = b.Color.R; _buffer[_pos++] = b.Color.G;
                        _buffer[_pos++] = b.Color.B; _buffer[_pos++] = b.Color.A;
                    }
                }
            }
        }

        private void WriteScreenOverlay(ScreenOverlayBuffer? buf)
        {
            if (buf == null || buf.Count == 0) return;

            var span = buf.GetSpan();
            int count = span.Length;

            int startPos = _pos;
            WriteSectionHeader(FrameProtocol.SectionScreenOverlay, (ushort)count, 0);

            for (int i = 0; i < count; i++)
            {
                ref readonly var item = ref span[i];
                EnsureCapacity(55);
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
            }

            int stringCount = 0;
            for (int i = 0; i < count; i++)
            {
                ref readonly var item = ref span[i];
                if (item.Kind == ScreenOverlayItemKind.Text)
                    stringCount = Math.Max(stringCount, item.StringId + 1);
            }

            EnsureCapacity(2);
            BinaryPrimitives.WriteUInt16LittleEndian(_buffer.AsSpan(_pos), (ushort)stringCount);
            _pos += 2;

            for (int i = 0; i < stringCount; i++)
            {
                string? s = buf.GetString(i);
                if (s == null) s = "";
                int byteCount = Encoding.UTF8.GetByteCount(s);
                EnsureCapacity(2 + byteCount);
                BinaryPrimitives.WriteUInt16LittleEndian(_buffer.AsSpan(_pos), (ushort)byteCount);
                _pos += 2;
                Encoding.UTF8.GetBytes(s, _buffer.AsSpan(_pos));
                _pos += byteCount;
            }

            int totalBytes = _pos - startPos - FrameProtocol.SectionHeaderSize;
            BinaryPrimitives.WriteInt32LittleEndian(_buffer.AsSpan(startPos + 3), totalBytes);
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

        private void SnapshotPrimitives(PrimitiveDrawBuffer? buf)
        {
            int count = buf?.Count ?? 0;
            if (_prevPrimitives.Length < count)
                _prevPrimitives = new PrimitiveDrawItem[count * 2];
            if (count > 0)
            {
                ReadOnlySpan<PrimitiveDrawItem> span = buf!.GetSpan();
                span.CopyTo(_prevPrimitives.AsSpan());
                _prevPrimitivesHaveStableIdentity = TryIndexPrimitivesByStableId(span, count, _prevPrimitiveIndices);
            }
            else
            {
                _prevPrimitiveIndices.Clear();
                _prevPrimitivesHaveStableIdentity = true;
            }
            _prevPrimitiveCount = count;
        }

        private static int HashBuffer(GroundOverlayBuffer? buf)
        {
            return buf?.Count ?? 0;
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

        private void WriteFloat(float value) { BinaryPrimitives.WriteSingleLittleEndian(_buffer.AsSpan(_pos), value); _pos += 4; }
        private void WriteInt32(int value) { BinaryPrimitives.WriteInt32LittleEndian(_buffer.AsSpan(_pos), value); _pos += 4; }
        private void WriteUInt32(uint value) { BinaryPrimitives.WriteUInt32LittleEndian(_buffer.AsSpan(_pos), value); _pos += 4; }
        private void WriteInt64(long value) { BinaryPrimitives.WriteInt64LittleEndian(_buffer.AsSpan(_pos), value); _pos += 8; }

        private void WritePrimitiveItem(in PrimitiveDrawItem item)
        {
            WriteInt32(item.MeshAssetId);
            WriteInt32(item.StableId);
            WriteFloat(item.Position.X); WriteFloat(item.Position.Y); WriteFloat(item.Position.Z);
            WriteFloat(item.Scale.X); WriteFloat(item.Scale.Y); WriteFloat(item.Scale.Z);
            WriteFloat(item.Color.X); WriteFloat(item.Color.Y); WriteFloat(item.Color.Z); WriteFloat(item.Color.W);
        }

        private static bool TryIndexPrimitivesByStableId(
            ReadOnlySpan<PrimitiveDrawItem> span,
            int count,
            Dictionary<int, int> indexMap)
        {
            indexMap.Clear();
            for (int i = 0; i < count; i++)
            {
                int stableId = span[i].StableId;
                if (stableId <= 0 || !indexMap.TryAdd(stableId, i))
                {
                    indexMap.Clear();
                    return false;
                }
            }

            return true;
        }

        private static bool WireEquals(in PrimitiveDrawItem a, in PrimitiveDrawItem b)
        {
            return a.MeshAssetId == b.MeshAssetId
                && a.StableId == b.StableId
                && a.Position.Equals(b.Position)
                && a.Scale.Equals(b.Scale)
                && a.Color.Equals(b.Color);
        }

        private void EnsureCapacity(int additionalBytes)
        {
            int required = _pos + additionalBytes;
            if (required <= _buffer.Length) return;
            Array.Resize(ref _buffer, Math.Max(_buffer.Length * 2, required));
        }
    }
}
