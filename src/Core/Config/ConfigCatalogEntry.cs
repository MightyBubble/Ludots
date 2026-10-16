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
        /// <summary>
        /// 这些键在深合并时整段换成后写的对象。视图名这类有序目录不能按字段并，否则下标会变。
        /// </summary>
        public readonly string[] ReplaceObjectFields;

        public ConfigCatalogEntry(
            string relativePath,
            ConfigMergePolicy mergePolicy,
            string? idField = null,
            string[]? arrayAppendFields = null,
            string[]? shardDirectories = null,
            bool allowEmpty = false,
            string[]? replaceObjectFields = null)
        {
            RelativePath = relativePath ?? string.Empty;
            MergePolicy = mergePolicy;
            IdField = string.IsNullOrWhiteSpace(idField) ? "id" : idField!;
            ArrayAppendFields = arrayAppendFields ?? System.Array.Empty<string>();
            ShardDirectories = shardDirectories ?? System.Array.Empty<string>();
            AllowEmpty = allowEmpty;
            ReplaceObjectFields = replaceObjectFields ?? System.Array.Empty<string>();
        }
    }
}
