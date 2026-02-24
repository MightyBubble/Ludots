namespace Ludots.Core.Navigation.GraphCore
{
    public readonly struct TagFilter256
    {
        public readonly TagBits256 RequiredAll;
        public readonly TagBits256 ForbiddenAny;

        public TagFilter256(in TagBits256 requiredAll, in TagBits256 forbiddenAny)
        {
            RequiredAll = requiredAll;
            ForbiddenAny = forbiddenAny;
        }

        public bool Matches(in TagBits256 tags)
        {
            if (!tags.ContainsAll(in RequiredAll)) return false;
            if (tags.Intersects(in ForbiddenAny)) return false;
            return true;
        }
    }
}

