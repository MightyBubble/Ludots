namespace ConfigurableDataSchemaSharedMod;

internal static class ConfigurableDataSchemaIds
{
    public const string MapId = "configurable_data_schema_workbench";
    public const string OwnerInstanceId = "data-schema-owner";
    public const string SchemaId = "unit";
    public const string WorkbenchRecordId = "unit.workbench";
    public const string ScoutPresetId = "unit.scout";
    public const string TankPresetId = "unit.tank";

    public const string PanelData = "panel.data.schema.workbench";
    public const string PanelGraph = "panel.graph.schema.workbench";
    public const string PanelMixed = "panel.mixed.schema.workbench";

    public const string InstalledKey = "ConfigurableDataSchema.Installed";
    public const string RuntimeServiceKey = "ConfigurableDataSchema.Runtime";

    public const string WorkbenchRootElementId = "data-schema-workbench-root";
    public const string ExportButtonElementId = "data-schema-export-button";

    public static bool IsShowcaseMap(string? mapId) =>
        string.Equals(mapId, MapId, StringComparison.Ordinal);
}
