using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Config;

public sealed class DataSchemaConfigLoader
{
    public const string SchemaConfigPath = "Data/data_schemas.json";
    public const string RecordConfigPath = "Data/data_records.json";

    private readonly ConfigPipeline _configs;

    public DataSchemaConfigLoader(ConfigPipeline configs)
    {
        _configs = configs ?? throw new ArgumentNullException(nameof(configs));
    }

    public DataSchemaRegistry Load(ConfigCatalog? catalog = null, ConfigConflictReport? report = null)
    {
        ConfigCatalogEntry schemaEntry = ConfigPipeline.RequireEntry(
            catalog, SchemaConfigPath, ConfigMergePolicy.ArrayById, "id");
        ConfigCatalogEntry recordEntry = ConfigPipeline.RequireEntry(
            catalog, RecordConfigPath, ConfigMergePolicy.ArrayById, "id");

        JsonArray schemas = ToArray(_configs.MergeArrayByIdFromCatalog(in schemaEntry, report), SchemaConfigPath);
        JsonArray records = ToArray(_configs.MergeArrayByIdFromCatalog(in recordEntry, report), RecordConfigPath);
        DataSchemaCatalog schemaCatalog = DataSchemaCatalog.Load(schemas);
        return DataSchemaRegistry.Load(schemaCatalog, records);
    }

    private static JsonArray ToArray(IReadOnlyList<MergedConfigEntry> entries, string path)
    {
        var array = new JsonArray();
        for (int i = 0; i < entries.Count; i++)
        {
            if (entries[i].Node is not JsonObject)
            {
                throw new InvalidOperationException($"Data config entry #{i} in '{path}' must be a JSON object.");
            }

            array.Add(entries[i].Node.DeepClone());
        }

        return array;
    }
}
