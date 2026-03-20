namespace Ludots.Core.Presentation.Performers
{
    public static class PerformerVisualIdentity
    {
        public static int ComposeStableId(int ownerStableId, PerformerVisualKind visualKind, int definitionId)
        {
            int hash = 19;
            hash = Mix(hash, ownerStableId);
            hash = Mix(hash, (int)visualKind);
            hash = Mix(hash, definitionId);
            return Finalize(hash);
        }

        private static int Mix(int hash, int value)
        {
            return unchecked((hash * 16777619) ^ value);
        }

        private static int Finalize(int hash)
        {
            hash &= int.MaxValue;
            return hash == 0 ? 1 : hash;
        }
    }
}
