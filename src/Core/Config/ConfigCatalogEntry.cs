namespace Ludots.Core.Config
{
    public readonly struct ConfigCatalogEntry
    {
        public readonly string RelativePath;
        public readonly ConfigMergePolicy MergePolicy;
        public readonly string IdField;
        public readonly string[] ArrayAppendFields;
        public readonly string[] ShardDirectories;
        public readonly bool AllowEmpty;

        public ConfigCatalogEntry(
            string relativePath,
            ConfigMergePolicy mergePolicy,
            string? idField = null,
            string[]? arrayAppendFields = null,
            string[]? shardDirectories = null,
            bool allowEmpty = false)
        {
            RelativePath = relativePath ?? string.Empty;
            MergePolicy = mergePolicy;
            IdField = string.IsNullOrWhiteSpace(idField) ? "id" : idField!;
            ArrayAppendFields = arrayAppendFields ?? System.Array.Empty<string>();
            ShardDirectories = shardDirectories ?? System.Array.Empty<string>();
            AllowEmpty = allowEmpty;
        }
    }
}
