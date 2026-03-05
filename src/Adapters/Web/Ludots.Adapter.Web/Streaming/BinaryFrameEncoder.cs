using System;
using System.Buffers.Binary;
using Ludots.Adapter.Web.Protocol;
using Ludots.Core.Presentation.Camera;
using Ludots.Core.Presentation.DebugDraw;
using Ludots.Core.Presentation.Hud;
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
            DebugDrawCommandBuffer? debugDraw)
        {
            _pos = 0;
            EnsureCapacity(FrameProtocol.FrameHeaderSize);

            _buffer[_pos++] = FrameProtocol.MsgTypeFrame;
            WriteUInt32(frameNumber);
            WriteInt32(simTick);
            WriteInt64(timestampMs);

            WriteCamera(in camera);
            WritePrimitives(primitives);
            WriteGroundOverlays(groundOverlays);
            WriteWorldHud(worldHud);
            WriteScreenHud(screenHud);
            WriteDebugDraw(debugDraw);

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
            int count = span.Length;
            int itemBytes = count * WirePrimitiveDrawItem.SizeInBytes;
            WriteSectionHeader(FrameProtocol.SectionPrimitives, (ushort)count, itemBytes);
            EnsureCapacity(itemBytes);

            for (int i = 0; i < count; i++)
            {
                ref readonly var item = ref span[i];
                WriteInt32(item.MeshAssetId);
                WriteFloat(item.Position.X); WriteFloat(item.Position.Y); WriteFloat(item.Position.Z);
                WriteFloat(item.Scale.X); WriteFloat(item.Scale.Y); WriteFloat(item.Scale.Z);
                WriteFloat(item.Color.X); WriteFloat(item.Color.Y); WriteFloat(item.Color.Z); WriteFloat(item.Color.W);
            }
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
            }
        }

        private void WriteScreenHud(ScreenHudBatchBuffer? buf)
        {
            if (buf == null || buf.Count == 0) return;

            var span = buf.GetSpan();
            int count = span.Length;
            int itemBytes = count * WireWorldHudItem.SizeInBytes;
            WriteSectionHeader(FrameProtocol.SectionScreenHud, (ushort)count, itemBytes);
            EnsureCapacity(itemBytes);

            for (int i = 0; i < count; i++)
            {
                ref readonly var item = ref span[i];
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
            }
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

        private void WriteSectionHeader(byte sectionType, ushort itemCount, int byteLength)
        {
            EnsureCapacity(FrameProtocol.SectionHeaderSize);
            _buffer[_pos++] = sectionType;
            BinaryPrimitives.WriteUInt16LittleEndian(_buffer.AsSpan(_pos), itemCount);
            _pos += 2;
            BinaryPrimitives.WriteInt32LittleEndian(_buffer.AsSpan(_pos), byteLength);
            _pos += 4;
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
