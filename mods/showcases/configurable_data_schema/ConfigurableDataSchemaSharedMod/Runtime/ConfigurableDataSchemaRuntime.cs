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
    private readonly DataSchemaAuthoringDocument _authoring = new();
    private readonly DataSchemaModAssetWriter _assetWriter = new();
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
    private bool _authoringLoaded;
    private Entity _owner = Entity.Null;

    public ConfigurableDataSchemaSnapshot Snapshot => BuildSnapshot();
    public DataSchemaAuthoringDocument Authoring => _authoring;
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
        EnsureAuthoringLoaded(engine);
        LoadPreset(engine, ConfigurableDataSchemaIds.ScoutPresetId, publish: true);
        ApplySourceMode(engine);
        _mapReady = true;
        _status = "先改 Scout 的坐标或稀有度，再看右侧面板；也可切到 Schema/Record/Binding 作者层写回 Mod。";
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

    public void SetAuthoringLayer(GameEngine engine, DataSchemaAuthoringLayer layer)
    {
        EnsureAuthoringLoaded(engine);
        _authoring.SetLayer(layer);
        SyncAuthoringPreview(engine);
        MountWorkbench(engine);
    }

    public void AuthoringAddField(GameEngine engine)
    {
        EnsureAuthoringLoaded(engine);
        _authoring.AddFieldToSelectedSchema();
        SyncAuthoringPreview(engine);
        MountWorkbench(engine);
    }

    public void AuthoringCycleFieldName(GameEngine engine)
    {
        EnsureAuthoringLoaded(engine);
        _authoring.CycleNewFieldName();
        MountWorkbench(engine);
    }

    public void AuthoringCycleFieldType(GameEngine engine)
    {
        EnsureAuthoringLoaded(engine);
        _authoring.CycleNewFieldType();
        MountWorkbench(engine);
    }

    public void AuthoringToggleRequired(GameEngine engine)
    {
        EnsureAuthoringLoaded(engine);
        _authoring.ToggleNewFieldRequired();
        MountWorkbench(engine);
    }

    public void AuthoringAddEpicEnum(GameEngine engine)
    {
        EnsureAuthoringLoaded(engine);
        _authoring.AddEnumMember("rarity", "Epic", 9);
        SyncAuthoringPreview(engine);
        MountWorkbench(engine);
    }

    public void AuthoringSelectRecord(GameEngine engine, string recordId)
    {
        EnsureAuthoringLoaded(engine);
        _authoring.SelectRecord(recordId);
        SyncAuthoringPreview(engine);
        MountWorkbench(engine);
    }

    public void AuthoringNudgeX(GameEngine engine, double delta)
    {
        EnsureAuthoringLoaded(engine);
        _authoring.NudgeSelectedRecordFloat("position.x", delta);
        SyncAuthoringPreview(engine);
        MountWorkbench(engine);
    }

    public void AuthoringCycleRarity(GameEngine engine)
    {
        EnsureAuthoringLoaded(engine);
        string[] names = _authoring.EnumerateEnumNames("rarity");
        if (names.Length == 0)
        {
            names = new[] { "Common", "Rare" };
        }

        _authoring.CycleSelectedRecordEnum("rarity", names);
        SyncAuthoringPreview(engine);
        MountWorkbench(engine);
    }

    public void AuthoringAddTag(GameEngine engine)
    {
        EnsureAuthoringLoaded(engine);
        _authoring.AddTag("authored");
        SyncAuthoringPreview(engine);
        MountWorkbench(engine);
    }

    public void AuthoringRemoveTag(GameEngine engine)
    {
        EnsureAuthoringLoaded(engine);
        _authoring.RemoveLastTag();
        SyncAuthoringPreview(engine);
        MountWorkbench(engine);
    }

    public void AuthoringSelectBindingPath(GameEngine engine, string path)
    {
        EnsureAuthoringLoaded(engine);
        _authoring.SelectBindingPath(path);
        MountWorkbench(engine);
    }

    public void AuthoringSelectPin(GameEngine engine, string pinName)
    {
        EnsureAuthoringLoaded(engine);
        _authoring.SelectPin(pinName);
        MountWorkbench(engine);
    }

    public void AuthoringSetPinSource(GameEngine engine, string source)
    {
        EnsureAuthoringLoaded(engine);
        _authoring.SetSelectedPinSource(source);
        MountWorkbench(engine);
    }

    public void SaveAuthoringToMod(GameEngine engine)
    {
        EnsureAuthoringLoaded(engine);
        DataSchemaModAssetWriteResult result = _authoring.Save(_assetWriter);
        if (result.Succeeded)
        {
            _status = _authoring.Status;
            _exportPath = _authoring.SaveTargetRoot;
            _isValid = true;
            _errorCount = 0;
            _firstErrorPath = string.Empty;
        }
        else
        {
            _status = _authoring.Status;
            _isValid = false;
            _errorCount = 1;
            _firstErrorPath = _authoring.FirstError;
        }

        MountWorkbench(engine);
    }

    public void RedirectAuthoringSaveRoot(GameEngine engine, string root)
    {
        EnsureAuthoringLoaded(engine);
        _authoring.RedirectSaveRootForTests(root);
    }

    private void EnsureAuthoringLoaded(GameEngine engine)
    {
        if (_authoringLoaded)
        {
            return;
        }

        DataSchemaRegistry startup = engine.DataSchemaRegistry
            ?? throw new InvalidOperationException("DataSchemaRegistry missing.");
        string saveRoot = ResolveShowcaseModRoot(engine);
        JsonArray panels = LoadPanelTemplatesJson(saveRoot);
        _authoring.LoadFromStartup(startup, panels, saveRoot);
        _authoringLoaded = true;
    }

    private void SyncAuthoringPreview(GameEngine engine)
    {
        DataSchemaProjectionSession session = engine.DataSchemaProjectionSession
            ?? throw new InvalidOperationException("DataSchemaProjectionSession missing.");
        _authoring.PublishWorkbenchRecord(session);
        if (!string.IsNullOrEmpty(_authoring.FirstError) && !_authoring.CanSave)
        {
            _isValid = false;
            _errorCount = 1;
            _firstErrorPath = _authoring.FirstError;
            _status = _authoring.Status;
        }
        else if (_authoring.GetSelectedRecordValue() is JsonObject value)
        {
            _draft = value.DeepClone().AsObject();
            _isValid = true;
            _errorCount = 0;
            _firstErrorPath = string.Empty;
            _status = _authoring.Status;
        }
    }

    private static string ResolveShowcaseModRoot(GameEngine engine)
    {
        if (engine.VFS.TryResolveFullPath("ConfigurableDataSchemaSharedMod:mod.json", out string modJson) &&
            !string.IsNullOrWhiteSpace(modJson))
        {
            string? root = Path.GetDirectoryName(modJson);
            if (!string.IsNullOrWhiteSpace(root) && Directory.Exists(root))
            {
                return root;
            }
        }

        string fallback = Path.Combine(
            FindRepoRoot(),
            "mods",
            "showcases",
            "configurable_data_schema",
            "ConfigurableDataSchemaSharedMod");
        if (!Directory.Exists(fallback))
        {
            throw new InvalidOperationException("ConfigurableDataSchemaSharedMod root was not found for authoring save.");
        }

        return fallback;
    }

    private static JsonArray LoadPanelTemplatesJson(string modRoot)
    {
        string path = Path.Combine(modRoot, "assets", "Panels", "panel_templates.json");
        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"Missing panel templates at {path}");
        }

        return JsonNode.Parse(File.ReadAllText(path)) as JsonArray
            ?? throw new InvalidOperationException("panel_templates.json must be an array.");
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
            Guide: "作者层可改 schema/record/绑定并写回 Mod；演示钮仍可快速消融对照。",
            ExportPath: _exportPath,
            CanExport: _isValid,
            ActivePanelId: ConfigurableDataSchemaDraft.PanelIdFor(_sourceMode),
            PositionX: x,
            UnitName: unitName,
            AuthoringLayer: _authoring.Layer,
            AuthoringStatus: _authoring.Status,
            AuthoringError: _authoring.FirstError,
            CanSaveToMod: _authoring.CanSave,
            SaveTargetRoot: _authoring.SaveTargetRoot,
            SelectedBindingPath: _authoring.SelectedBindingPath,
            SelectedPinName: _authoring.SelectedPinName,
            NewFieldName: _authoring.NewFieldName,
            NewFieldType: _authoring.NewFieldType,
            NewFieldRequired: _authoring.NewFieldRequired,
            AuthoringRecordSummary: BuildAuthoringRecordSummary());
    }

    private string BuildAuthoringRecordSummary()
    {
        JsonObject? value = _authoring.GetSelectedRecordValue();
        if (value == null)
        {
            return "(no record)";
        }

        string name = value["name"] is JsonValue nameValue && nameValue.TryGetValue<string>(out string? text)
            ? text ?? "?"
            : "?";
        int tags = value["tags"] is JsonArray array ? array.Count : 0;
        return $"{_authoring.SelectedRecordId} · {name} · tags={tags}";
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
