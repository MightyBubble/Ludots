namespace Ludots.Adapter.Web.Protocol
{
    /// <summary>
    /// Wire constants for server → client binary frame protocol.
    /// [MsgType(1)] [FrameNumber(4)] [SimTick(4)] [Timestamp(8)] [Sections...] [SectionEnd]
    /// Each Section: [SectionType(1)] [ItemCount(2)] [ByteLength(4)] [ItemBytes...]
    /// </summary>
    public static class FrameProtocol
    {
        public const byte MsgTypeFrame = 0x01;
        public const byte MsgTypeDelta = 0x05;

        public const byte SectionEnd = 0x00;
        public const byte SectionCamera = 0x01;
        public const byte SectionPrimitives = 0x02;
        public const byte SectionGroundOverlays = 0x03;
        public const byte SectionWorldHud = 0x04;
        public const byte SectionScreenHud = 0x05;
        public const byte SectionDebugLines = 0x10;
        public const byte SectionDebugCircles = 0x11;
        public const byte SectionDebugBoxes = 0x12;
        public const byte SectionPrimitivesDelta = 0x18;

        public const int FrameHeaderSize = 1 + 4 + 4 + 8; // MsgType + FrameNum + SimTick + Timestamp
        public const int SectionHeaderSize = 1 + 2 + 4;    // SectionType + ItemCount + ByteLength
    }

    public static class WireCameraState
    {
        public const int SizeInBytes = 10 * 4; // 3 vec3 (pos, target, up) + fov = 10 floats
    }

    public static class WirePrimitiveDrawItem
    {
        public const int SizeInBytes = 44; // meshAssetId(4) + pos(12) + scale(12) + color(16)
    }

    public static class WireGroundOverlayItem
    {
        public const int SizeInBytes = 69; // shape(1) + center(12) + 5floats(20) + fill(16) + border(16) + borderWidth(4)
    }

    public static class WireWorldHudItem
    {
        // kind(1) + worldPos(12) + color0(16) + color1(16) + width(4) + height(4) + v0(4) + v1(4) + id0(4) + id1(4) + fontSize(4) = 73
        public const int SizeInBytes = 73;
    }

    public static class WireDebugLine
    {
        public const int SizeInBytes = 24; // a(8) + b(8) + thickness(4) + color(4)
    }

    public static class WireDebugCircle
    {
        public const int SizeInBytes = 20; // center(8) + radius(4) + thickness(4) + color(4)
    }

    public static class WireDebugBox
    {
        public const int SizeInBytes = 28; // center(8) + halfW(4) + halfH(4) + rotation(4) + thickness(4) + color(4)
    }
}
