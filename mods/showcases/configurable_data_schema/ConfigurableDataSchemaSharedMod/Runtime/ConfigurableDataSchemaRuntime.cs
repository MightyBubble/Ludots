using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Config;
using Ludots.Core.Engine;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using Ludots.Core.UI.PanelActivation;
using Ludots.Core.UI.PanelHosting;
using Ludots.UI;
using ConfigurableDataSchemaSharedMod.UI;

namespace ConfigurableDataSchemaSharedMod.Runtime;

public sealed class ConfigurableDataSchemaRuntime
{
    private readonly ConfigurableDataSchemaWorkbenchController _workbench = new();
    private JsonObject _draft = new();
    private string _presetRecordId = ConfigurableDataSchemaIds.ScoutPresetId;
    private DataSchemaSourceMode _sourceMode = DataSchemaSourceMode.Mixed;
    private DataSchemaBindingFocus _bindingFocus = DataSchemaBindingFocus.PositionX;
    private DataSchemaInvalidCase _invalidCase = DataSchemaInvalidCase.None;
    private bool _isValid = true;
    private int _errorCount;
    private string _firstErrorPath = string.Empty;
    private string _status = "加载数据结构工作台。";
    private string _exportPath = string.Empty;
    private bool _mapReady;
    private Entity _owner = Entity.Null;

    public ConfigurableDataSchemaSnapshot Snapshot => BuildSnapshot();

    public Task HandleMapFocusedAsync(ScriptContext context)
    {
        GameEngine? engine = context.GetEngine();
        if (engine == null)
        {
            return Task.CompletedTask;
        }

        if (!ConfigurableDataSchemaIds.IsShowcaseMap(engine.CurrentMapSession?.MapId.Value) &&
            !ConfigurableDataSchemaIds.IsShowcaseMap(context.Get(CoreServiceKeys.MapId).Value))
        {
            return Task.CompletedTask;
        }

        EnsureOwner(engine);
        EnsurePanels(engine);
        LoadPreset(engine, ConfigurableDataSchemaIds.ScoutPresetId, publish: true);
        ApplySourceMode(engine);
        _mapReady = true;
        _status = "先改 Scout 的坐标或稀有度，再看右侧面板；故意填错时，导出必须停住并指出字段路径。";
        MountWorkbench(engine);
        return Task.CompletedTask;
    }

    public Task HandleMapUnloadedAsync(ScriptContext context)
    {
        GameEngine? engine = context.GetEngine();
        if (engine?.GetService(CoreServiceKeys.UIRoot) is UIRoot root)
        {
            _workbench.ClearIfOwned(root);
        }

        engine?.GetService(CoreServiceKeys.DataSchemaProjectionSession)?.UseStartup();
        _mapReady = false;
        _owner = Entity.Null;
        return Task.CompletedTask;
    }

    public void TickPresentation(GameEngine engine)
    {
        if (!_mapReady)
        {
            return;
        }

        MountWorkbench(engine);
    }

    public void SelectPreset(GameEngine engine, string presetRecordId)
    {
        LoadPreset(engine, presetRecordId, publish: true);
        MountWorkbench(engine);
    }

    public void CycleSourceMode(GameEngine engine)
    {
        _sourceMode = _sourceMode switch
        {
            DataSchemaSourceMode.Mixed => DataSchemaSourceMode.Data,
            DataSchemaSourceMode.Data => DataSchemaSourceMode.Graph,
            _ => DataSchemaSourceMode.Mixed,
        };
        ApplySourceMode(engine);
        _status = $"数据来源切换为 {_sourceMode}。换肤不会改变来源。";
        MountWorkbench(engine);
    }

    public void CycleBindingFocus(GameEngine engine)
    {
        _bindingFocus = _bindingFocus switch
        {
            DataSchemaBindingFocus.PositionX => DataSchemaBindingFocus.Rarity,
            DataSchemaBindingFocus.Rarity => DataSchemaBindingFocus.Tags,
            DataSchemaBindingFocus.Tags => DataSchemaBindingFocus.Name,
            _ => DataSchemaBindingFocus.PositionX,
        };
        _status = $"当前观察绑定路径 {ConfigurableDataSchemaDraft.BindingPathFor(_bindingFocus)}。";
        MountWorkbench(engine);
    }

    public void NudgePositionX(GameEngine engine, float delta)
    {
        EnsureDraftShape();
        JsonObject position = RequireObject(_draft, "position");
        float current = ReadFloat(position, "x");
        position["x"] = (double)(current + delta);
        _invalidCase = DataSchemaInvalidCase.None;
        PublishDraft(engine);
        MountWorkbench(engine);
    }

    public void CycleRarity(GameEngine engine)
    {
        EnsureDraftShape();
        string current = ReadString(_draft, "rarity");
        _draft["rarity"] = string.Equals(current, "Rare", StringComparison.Ordinal) ? "Common" : "Rare";
        _invalidCase = DataSchemaInvalidCase.None;
        PublishDraft(engine);
        MountWorkbench(engine);
    }

    public void InjectInvalid(GameEngine engine, DataSchemaInvalidCase kind)
    {
        if (kind == DataSchemaInvalidCase.None)
        {
            LoadPreset(engine, _presetRecordId, publish: true);
            MountWorkbench(engine);
            return;
        }

        EnsureDraftShape();
        _invalidCase = kind;
        if (kind == DataSchemaInvalidCase.MissingRequired)
        {
            _draft.Remove("name");
        }
        else if (kind == DataSchemaInvalidCase.UnknownEnum)
        {
            _draft["rarity"] = "Legendary";
        }

        PublishDraft(engine);
        MountWorkbench(engine);
    }

    public void ExportAuthorAssets(GameEngine engine)
    {
        if (!_isValid)
        {
            _status = "校验未通过，导出已禁用。";
            MountWorkbench(engine);
            return;
        }

        string repoRoot = FindRepoRoot();
        string exportDir = Path.Combine(
            repoRoot,
            "artifacts",
            "acceptance",
            "configurable-data-schema-showcase",
            "exported");
        Directory.CreateDirectory(exportDir);

        string schemasPath = Path.Combine(exportDir, "data_schemas.json");
        string recordsPath = Path.Combine(exportDir, "data_records.json");

        DataSchemaRegistry startup = engine.DataSchemaRegistry
            ?? throw new InvalidOperationException("DataSchemaRegistry missing.");
        File.WriteAllText(schemasPath, SerializeCatalog(startup.Catalog));
        File.WriteAllText(recordsPath, SerializeRecordsWithWorkbench(startup, _draft));

        _exportPath = exportDir;
        _status = $"已导出作者资产到 {exportDir}";
        MountWorkbench(engine);
    }

    private void LoadPreset(GameEngine engine, string presetRecordId, bool publish)
    {
        DataSchemaRegistry startup = engine.DataSchemaRegistry
            ?? throw new InvalidOperationException("DataSchemaRegistry missing.");
        if (!startup.TryGet(presetRecordId, out DataSchemaRecord preset))
        {
            throw new InvalidOperationException($"Preset record '{presetRecordId}' is missing.");
        }

        _presetRecordId = presetRecordId;
        _draft = preset.Value.DeepClone().AsObject();
        _invalidCase = DataSchemaInvalidCase.None;
        if (publish)
        {
            PublishDraft(engine);
        }
    }

    private void PublishDraft(GameEngine engine)
    {
        DataSchemaProjectionSession session = engine.DataSchemaProjectionSession
            ?? throw new InvalidOperationException("DataSchemaProjectionSession missing.");

        if (!session.TryPublishRecordDraft(
                ConfigurableDataSchemaIds.WorkbenchRecordId,
                ConfigurableDataSchemaIds.SchemaId,
                _draft,
                out string error))
        {
            _isValid = false;
            _errorCount = 1;
            _firstErrorPath = ExtractPath(error);
            _status = $"校验失败：{error}";
            _exportPath = string.Empty;
            return;
        }

        _isValid = true;
        _errorCount = 0;
        _firstErrorPath = string.Empty;
        _status = _invalidCase == DataSchemaInvalidCase.None
            ? $"工作台记录已更新（来自 {_presetRecordId}）。"
            : "非法用例已清除。";
    }

    private void ApplySourceMode(GameEngine engine)
    {
        PanelActivationApi activation = engine.GetService(CoreServiceKeys.PanelActivationApi)
            ?? throw new InvalidOperationException("PanelActivationApi missing.");

        string active = ConfigurableDataSchemaDraft.PanelIdFor(_sourceMode);
        Hide(activation, ConfigurableDataSchemaIds.PanelData);
        Hide(activation, ConfigurableDataSchemaIds.PanelGraph);
        Hide(activation, ConfigurableDataSchemaIds.PanelMixed);
        activation.ShowPanel(active);
    }

    private static void Hide(PanelActivationApi activation, string panelType)
    {
        activation.HidePanel(panelType);
    }

    private void EnsurePanels(GameEngine engine)
    {
        PanelHost host = engine.GetService(CoreServiceKeys.PanelHost)
            ?? throw new InvalidOperationException("PanelHost missing.");

        EnsurePanel(host, ConfigurableDataSchemaIds.PanelData);
        EnsurePanel(host, ConfigurableDataSchemaIds.PanelGraph);
        EnsurePanel(host, ConfigurableDataSchemaIds.PanelMixed);
    }

    private void EnsurePanel(PanelHost host, string templateId)
    {
        foreach (PanelHostInstanceInfo info in host.SnapshotInstances())
        {
            if (string.Equals(info.TemplateId, templateId, StringComparison.Ordinal))
            {
                return;
            }
        }

        host.Instantiate(templateId, "screen.topRight", _owner);
    }

    private void EnsureOwner(GameEngine engine)
    {
        World world = engine.World;
        var query = new QueryDescription().WithAll<Name>();
        world.Query(in query, (Entity entity, ref Name name) =>
        {
            if (string.Equals(name.Value, "WorkbenchOwner", StringComparison.Ordinal))
            {
                _owner = entity;
            }
        });

        if (_owner == Entity.Null)
        {
            throw new InvalidOperationException("Workbench owner entity was not found after map load.");
        }
    }

    private void MountWorkbench(GameEngine engine)
    {
        if (engine.GetService(CoreServiceKeys.UIRoot) is not UIRoot root)
        {
            return;
        }

        _workbench.MountOrRefresh(root, engine, Snapshot);
    }

    private ConfigurableDataSchemaSnapshot BuildSnapshot()
    {
        EnsureDraftShape(allowMissingName: !_isValid && _invalidCase == DataSchemaInvalidCase.MissingRequired);
        string bindingPath = ConfigurableDataSchemaDraft.BindingPathFor(_bindingFocus);
        ReadBinding(_draft, _bindingFocus, out string valueText, out string typeText, out int tagCount, out string rarityName, out int rarityValue, out float x, out string unitName);

        return new ConfigurableDataSchemaSnapshot(
            SchemaId: ConfigurableDataSchemaIds.SchemaId,
            PresetRecordId: _presetRecordId,
            WorkbenchRecordId: ConfigurableDataSchemaIds.WorkbenchRecordId,
            BindingPath: bindingPath,
            BindingValueText: valueText,
            BindingTypeText: typeText,
            TagCount: tagCount,
            RarityName: rarityName,
            RarityValue: rarityValue,
            SourceMode: _sourceMode,
            BindingFocus: _bindingFocus,
            InvalidCase: _invalidCase,
            IsValid: _isValid,
            ErrorCount: _errorCount,
            FirstErrorPath: _firstErrorPath,
            Status: _status,
            Guide: "改坐标/稀有度看右侧面板；切换 Graph/Data/Mixed；故意填错后导出必须停住。",
            ExportPath: _exportPath,
            CanExport: _isValid,
            ActivePanelId: ConfigurableDataSchemaDraft.PanelIdFor(_sourceMode),
            PositionX: x,
            UnitName: unitName);
    }

    private void EnsureDraftShape(bool allowMissingName = false)
    {
        if (!_draft.ContainsKey("position") || _draft["position"] is not JsonObject)
        {
            _draft["position"] = new JsonObject { ["x"] = 0f, ["y"] = 0f };
        }

        if (!_draft.ContainsKey("tags") || _draft["tags"] is not JsonArray)
        {
            _draft["tags"] = new JsonArray();
        }

        if (!_draft.ContainsKey("rarity"))
        {
            _draft["rarity"] = "Common";
        }

        if (!allowMissingName && !_draft.ContainsKey("name"))
        {
            _draft["name"] = "Scout";
        }
    }

    private static void ReadBinding(
        JsonObject draft,
        DataSchemaBindingFocus focus,
        out string valueText,
        out string typeText,
        out int tagCount,
        out string rarityName,
        out int rarityValue,
        out float x,
        out string unitName)
    {
        JsonArray tags = draft["tags"] as JsonArray ?? new JsonArray();
        tagCount = tags.Count;
        rarityName = draft.TryGetPropertyValue("rarity", out JsonNode? rarityNode) && rarityNode is JsonValue rarityValueNode
            ? rarityValueNode.GetValue<string>()
            : string.Empty;
        rarityValue = string.Equals(rarityName, "Rare", StringComparison.Ordinal) ? 5
            : string.Equals(rarityName, "Common", StringComparison.Ordinal) ? 1
            : 0;
        JsonObject position = draft["position"] as JsonObject ?? new JsonObject();
        x = ReadFloat(position, "x");
        unitName = draft.TryGetPropertyValue("name", out JsonNode? nameNode) && nameNode is JsonValue nameValue
            ? nameValue.GetValue<string>()
            : "(missing)";

        switch (focus)
        {
            case DataSchemaBindingFocus.Name:
                valueText = unitName;
                typeText = "string";
                break;
            case DataSchemaBindingFocus.PositionX:
                valueText = x.ToString("0.###", CultureInfo.InvariantCulture);
                typeText = "float";
                break;
            case DataSchemaBindingFocus.Tags:
                valueText = tags.ToJsonString();
                typeText = "array<string>";
                break;
            case DataSchemaBindingFocus.Rarity:
                valueText = string.IsNullOrEmpty(rarityName) ? "(invalid)" : $"{rarityName} ({rarityValue})";
                typeText = "enum:rarity";
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(focus));
        }
    }

    private static JsonObject RequireObject(JsonObject parent, string name)
    {
        if (parent[name] is JsonObject obj)
        {
            return obj;
        }

        var created = new JsonObject();
        parent[name] = created;
        return created;
    }

    private static float ReadFloat(JsonObject obj, string name)
    {
        if (obj[name] is not JsonValue value)
        {
            return 0f;
        }

        if (value.TryGetValue<double>(out double raw))
        {
            return (float)raw;
        }

        if (value.TryGetValue<float>(out float asFloat))
        {
            return asFloat;
        }

        if (value.TryGetValue<int>(out int asInt))
        {
            return asInt;
        }

        return 0f;
    }

    private static string ReadString(JsonObject obj, string name)
    {
        if (obj[name] is JsonValue value && value.TryGetValue<string>(out string? text) && text != null)
        {
            return text;
        }

        return string.Empty;
    }

    private static string ExtractPath(string error)
    {
        const string marker = "record '";
        int start = error.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0)
        {
            return error;
        }

        return error;
    }

    private static string SerializeCatalog(DataSchemaCatalog catalog)
    {
        var array = new JsonArray();
        foreach (DataSchemaDefinition definition in catalog.Definitions)
        {
            array.Add(SerializeDefinition(definition));
        }

        return array.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private static JsonObject SerializeDefinition(DataSchemaDefinition definition)
    {
        var obj = new JsonObject
        {
            ["id"] = definition.Id,
            ["kind"] = definition.Kind == DataSchemaDefinitionKind.Enum ? "enum" : "struct",
        };

        if (definition.Kind == DataSchemaDefinitionKind.Enum)
        {
            var values = new JsonArray();
            foreach (DataSchemaEnumValue value in definition.EnumValues)
            {
                values.Add(new JsonObject { ["name"] = value.Name, ["value"] = value.Value });
            }

            obj["values"] = values;
            return obj;
        }

        var fields = new JsonArray();
        foreach (DataSchemaField field in definition.Fields)
        {
            fields.Add(new JsonObject
            {
                ["name"] = field.Name,
                ["type"] = SerializeType(field.Type),
                ["required"] = field.Required,
            });
        }

        obj["fields"] = fields;
        return obj;
    }

    private static JsonNode SerializeType(DataSchemaType type)
    {
        return type.Kind switch
        {
            DataSchemaTypeKind.String => JsonValue.Create("string")!,
            DataSchemaTypeKind.Int => JsonValue.Create("int")!,
            DataSchemaTypeKind.Float => JsonValue.Create("float")!,
            DataSchemaTypeKind.Bool => JsonValue.Create("bool")!,
            DataSchemaTypeKind.EntityRef => JsonValue.Create("entityRef")!,
            DataSchemaTypeKind.Struct => new JsonObject { ["kind"] = "struct", ["ref"] = type.Reference },
            DataSchemaTypeKind.Enum => new JsonObject { ["kind"] = "enum", ["ref"] = type.Reference },
            DataSchemaTypeKind.Array => new JsonObject
            {
                ["kind"] = "array",
                ["items"] = SerializeType(type.ElementType!),
            },
            _ => throw new InvalidOperationException($"Unsupported type '{type.Kind}'."),
        };
    }

    private static string SerializeRecordsWithWorkbench(DataSchemaRegistry startup, JsonObject workbenchDraft)
    {
        var array = new JsonArray();
        foreach (DataSchemaRecord record in startup.Records)
        {
            JsonObject value = string.Equals(record.Id, ConfigurableDataSchemaIds.WorkbenchRecordId, StringComparison.Ordinal)
                ? workbenchDraft.DeepClone().AsObject()
                : record.Value.DeepClone().AsObject();
            array.Add(new JsonObject
            {
                ["id"] = record.Id,
                ["schema"] = record.SchemaId,
                ["value"] = value,
            });
        }

        return array.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private static string FindRepoRoot()
    {
        string? dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            if (File.Exists(Path.Combine(dir, "showcase.registry.json")))
            {
                return dir;
            }

            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new InvalidOperationException("Repository root with showcase.registry.json was not found.");
    }
}
