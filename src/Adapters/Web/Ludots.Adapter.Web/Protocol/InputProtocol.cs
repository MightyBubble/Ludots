namespace Ludots.Adapter.Web.Protocol
{
    public static class InputProtocol
    {
        public const byte MsgTypeInputState = 0x81;
        public const byte MsgTypePointerEvent = 0x82;

        public const int InputStateMessageSize = 33;
        public const int PointerEventMessageSize = 30;

        public const int InputStateButtonMaskOffset = 1;
        public const int InputStateMouseXOffset = 5;
        public const int InputStateMouseYOffset = 9;
        public const int InputStateMouseWheelOffset = 13;
        public const int InputStateKeyBitsOffset = 17;
        public const int InputStateViewportWidthOffset = 25;
        public const int InputStateViewportHeightOffset = 29;

        public const int PointerActionOffset = 1;
        public const int PointerButtonMaskOffset = 2;
        public const int PointerXOffset = 6;
        public const int PointerYOffset = 10;
        public const int PointerDeltaXOffset = 14;
        public const int PointerDeltaYOffset = 18;
        public const int PointerViewportWidthOffset = 22;
        public const int PointerViewportHeightOffset = 26;

        public const int ButtonMaskLeft = 1 << 0;
        public const int ButtonMaskRight = 1 << 1;
        public const int ButtonMaskMiddle = 1 << 2;
    }
}
