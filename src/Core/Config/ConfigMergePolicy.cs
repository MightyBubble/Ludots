namespace Ludots.Core.Config
{
    public enum ConfigMergePolicy : byte
    {
        Replace = 0,
        DeepObject = 1,
        ArrayReplace = 2,
        ArrayAppend = 3,
        ArrayById = 4
    }
}

