namespace Ludots.Adapter.Web.Protocol
{
    /// <summary>
    /// Wire constants for client → server binary input messages.
    /// Input: [MsgType(1)] [ButtonMask(4)] [MouseX(4)] [MouseY(4)] [MouseWheel(4)] [KeyBitfield(8)]
    /// </summary>
    public static class InputProtocol
    {
        public const byte MsgTypeInput = 0x81;
        public const int InputMessageSize = 1 + 4 + 4 + 4 + 4 + 8; // 25 bytes

        public const int ButtonMaskLeft = 1 << 0;
        public const int ButtonMaskRight = 1 << 1;
        public const int ButtonMaskMiddle = 1 << 2;
    }
}
