namespace Ludots.Core.Gameplay.GAS.Components
{
    public struct GameplayControlState
    {
        public byte MoveBlocked;
        public byte ActionBlocked;

        public static GameplayControlState CreateDefault()
        {
            return default;
        }

        public void Reset()
        {
            MoveBlocked = 0;
            ActionBlocked = 0;
        }

        public readonly bool IsMoveBlocked()
        {
            return MoveBlocked != 0;
        }
    }
}
