using System.Text.Json.Nodes;

namespace ConfigurableDataSchemaSharedMod.Runtime;

public enum DataSchemaSourceMode : byte
{
    Data = 1,
    Graph = 2,
    Mixed = 3,
}

public enum DataSchemaBindingFocus : byte
{
    Name = 1,
    PositionX = 2,
    Tags = 3,
    Rarity = 4,
}

public enum DataSchemaInvalidCase : byte
{
    None = 0,
    MissingRequired = 1,
    UnknownEnum = 2,
}

public readonly record struct ConfigurableDataSchemaSnapshot(
    string SchemaId,
    string PresetRecordId,
    string WorkbenchRecordId,
    string BindingPath,
    string BindingValueText,
    string BindingTypeText,
    int TagCount,
    string RarityName,
    int RarityValue,
    DataSchemaSourceMode SourceMode,
    DataSchemaBindingFocus BindingFocus,
    DataSchemaInvalidCase InvalidCase,
    bool IsValid,
    int ErrorCount,
    string FirstErrorPath,
    string Status,
    string Guide,
    string ExportPath,
    bool CanExport,
    string ActivePanelId,
    float PositionX,
    string UnitName,
    DataSchemaAuthoringLayer AuthoringLayer,
    string AuthoringStatus,
    string AuthoringError,
    bool CanSaveToMod,
    string SaveTargetRoot,
    string SelectedBindingPath,
    string SelectedPinName,
    string NewFieldName,
    string NewFieldType,
    bool NewFieldRequired,
    string AuthoringRecordSummary);

internal static class ConfigurableDataSchemaDraft
{
    public static string BindingPathFor(DataSchemaBindingFocus focus) => focus switch
    {
        DataSchemaBindingFocus.Name => "name",
        DataSchemaBindingFocus.PositionX => "position.x",
        DataSchemaBindingFocus.Tags => "tags",
        DataSchemaBindingFocus.Rarity => "rarity",
        _ => throw new ArgumentOutOfRangeException(nameof(focus)),
    };

    public static string PanelIdFor(DataSchemaSourceMode mode) => mode switch
    {
        DataSchemaSourceMode.Data => ConfigurableDataSchemaIds.PanelData,
        DataSchemaSourceMode.Graph => ConfigurableDataSchemaIds.PanelGraph,
        DataSchemaSourceMode.Mixed => ConfigurableDataSchemaIds.PanelMixed,
        _ => throw new ArgumentOutOfRangeException(nameof(mode)),
    };
}
