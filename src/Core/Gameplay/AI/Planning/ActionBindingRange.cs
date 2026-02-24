namespace Ludots.Core.Gameplay.AI.Planning
{
    public readonly struct ActionBindingRange
    {
        public readonly int Offset;
        public readonly int Count;

        public ActionBindingRange(int offset, int count)
        {
            Offset = offset;
            Count = count;
        }
    }
}

