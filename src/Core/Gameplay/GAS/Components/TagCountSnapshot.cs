namespace Ludots.Core.Gameplay.GAS.Components
{
    public unsafe struct TagCountSnapshot
    {
        public fixed int TagIds[TagCountContainer.CAPACITY];
        public fixed ushort Counts[TagCountContainer.CAPACITY];
        public int Count;

        public ushort GetCount(int tagId)
        {
            if (tagId < 0) return 0;
            for (int i = 0; i < Count; i++)
            {
                if (TagIds[i] == tagId) return Counts[i];
            }
            return 0;
        }

        public void SetCount(int tagId, ushort newCount)
        {
            if (tagId < 0) return;

            for (int i = 0; i < Count; i++)
            {
                if (TagIds[i] != tagId) continue;
                if (newCount == 0)
                {
                    TagIds[i] = TagIds[Count - 1];
                    Counts[i] = Counts[Count - 1];
                    Count--;
                    return;
                }
                Counts[i] = newCount;
                return;
            }

            if (newCount == 0) return;
            if (Count >= TagCountContainer.CAPACITY) return;
            TagIds[Count] = tagId;
            Counts[Count] = newCount;
            Count++;
        }

        public static TagCountSnapshot From(ref TagCountContainer src)
        {
            var snap = default(TagCountSnapshot);
            snap.Count = src.Count;
            for (int i = 0; i < src.Count; i++)
            {
                snap.TagIds[i] = src.TagIds[i];
                snap.Counts[i] = src.Counts[i];
            }
            return snap;
        }
    }
}
