namespace Ludots.Core.Config
{
    public readonly struct ConfigCatalogEntry
    {
        public readonly string RelativePath;
        public readonly ConfigMergePolicy MergePolicy;
        public readonly string IdField;
        public readonly string[] ArrayAppendFields;

        public ConfigCatalogEntry(string relativePath, ConfigMergePolicy mergePolicy, string? idField = null, string[]? arrayAppendFields = null)
        {
            RelativePath = relativePath ?? string.Empty;
            MergePolicy = mergePolicy;
            IdField = string.IsNullOrWhiteSpace(idField) ? "Id" : idField!;
            ArrayAppendFields = arrayAppendFields ?? System.Array.Empty<string>();
        }
    }
}
