namespace Ludots.Adapter.Web.Protocol
{
    public static class FrameProtocol
    {
        public const byte MsgTypeFrame = 0x01;
        public const byte MsgTypeMeshMap = 0x03;
        public const byte MsgTypeDelta = 0x05;

        public const byte SectionEnd = 0x00;
        public const byte SectionCamera = 0x01;
        public const byte SectionPrimitives = 0x02;
        public const byte SectionGroundOverlays = 0x03;
        public const byte SectionWorldHud = 0x04;
        public const byte SectionScreenHud = 0x05;
        public const byte SectionUiScene = 0x09;
        public const byte SectionScreenOverlay = 0x0A;
        public const byte SectionDebugLines = 0x10;
        public const byte SectionDebugCircles = 0x11;
        public const byte SectionDebugBoxes = 0x12;
        public const byte SectionPrimitivesDelta = 0x18;

        public const int FrameHeaderSize = 1 + 4 + 4 + 8;
        public const int SectionHeaderSize = 1 + 2 + 4;
    }

    public static class WireCameraState
    {
        public const int SizeInBytes = 10 * 4;
    }

    public static class WirePrimitiveDrawItem
    {
        public const int SizeInBytes = 48;
    }

    public static class WireGroundOverlayItem
    {
        public const int SizeInBytes = 73;
    }

    public static class WireWorldHudItem
    {
        public const int SizeInBytes = 73;
    }

    public static class WireDebugLine
    {
        public const int SizeInBytes = 24;
    }

    public static class WireDebugCircle
    {
        public const int SizeInBytes = 20;
    }

    public static class WireDebugBox
    {
        public const int SizeInBytes = 28;
    }
}
