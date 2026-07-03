namespace Ludots.Core.Gameplay.GAS.Orders
{
    public readonly struct BlackboardStoredTargetKeys
    {
        public BlackboardStoredTargetKeys(
            int targetKindKey,
            int targetPositionKey,
            int targetEntityKey,
            int hexQKey,
            int hexRKey)
        {
            TargetKindKey = targetKindKey;
            TargetPositionKey = targetPositionKey;
            TargetEntityKey = targetEntityKey;
            HexQKey = hexQKey;
            HexRKey = hexRKey;
        }

        public int TargetKindKey { get; }
        public int TargetPositionKey { get; }
        public int TargetEntityKey { get; }
        public int HexQKey { get; }
        public int HexRKey { get; }

        public bool IsConfigured =>
            TargetKindKey >= 0 &&
            TargetPositionKey >= 0 &&
            TargetEntityKey >= 0 &&
            HexQKey >= 0 &&
            HexRKey >= 0;
    }
}
