using System.Globalization;
using System.Text.Json.Nodes;
using Ludots.Core.Config;

namespace ConfigurableDataSchemaSharedMod.Runtime;

public enum DataSchemaAuthoringLayer : byte
{
    Schema = 1,
    Record = 2,
    Binding = 3,
    Preview = 4,
}

public readonly record struct AuthoringFormField(
    string Path,
    string Kind,
    string ValueText,
    string TypeRef,
    bool Required,
    int ArrayLength);

public sealed class DataSchemaAuthoringDocument
{
    private JsonArray _schemas = new();
    private JsonArray _records = new();
    private JsonArray _panels = new();
    private string _selectedSchemaId = ConfigurableDataSchemaIds.SchemaId;
    private string _selectedRecordId = ConfigurableDataSchemaIds.WorkbenchRecordId;
    private string _selectedPanelId = ConfigurableDataSchemaIds.PanelMixed;
    private string _selectedPinName = "x";
    private string _selectedBindingPath = "position.x";
    private DataSchemaAuthoringLayer _layer = DataSchemaAuthoringLayer.Record;
    private string _newFieldName = "notes";
    private string _newFieldType = "string";
    private bool _newFieldRequired = true;
    private string _status = "作者编辑器就绪。";
    private string _firstError = string.Empty;
    private bool _canSave;
    private string _saveTargetRoot = string.Empty;

    public DataSchemaAuthoringLayer Layer => _layer;
    public string SelectedSchemaId => _selectedSchemaId;
    public string SelectedRecordId => _selectedRecordId;
    public string SelectedPanelId => _selectedPanelId;
    public string SelectedPinName => _selectedPinName;
    public string SelectedBindingPath => _selectedBindingPath;
    public string NewFieldName => _newFieldName;
    public string NewFieldType => _newFieldType;
    public bool NewFieldRequired => _newFieldRequired;
    public string Status => _status;
    public string FirstError => _firstError;
    public bool CanSave => _canSave;
    public string SaveTargetRoot => _saveTargetRoot;
    public JsonArray Schemas => _schemas;
    public JsonArray Records => _records;
    public JsonArray Panels => _panels;

    public void RedirectSaveRootForTests(string root)
    {
        if (string.IsNullOrWhiteSpace(root))
        {
            throw new ArgumentException("Save root is required.", nameof(root));
        }

        _saveTargetRoot = root;
        Revalidate();
    }

    public void LoadFromStartup(DataSchemaRegistry startup, JsonArray panelTemplates, string saveTargetRoot)
    {
        ArgumentNullException.ThrowIfNull(startup);
        ArgumentNullException.ThrowIfNull(panelTemplates);
        _saveTargetRoot = saveTargetRoot ?? string.Empty;
        _schemas = SerializeCatalog(startup.Catalog);
        _records = SerializeRecords(startup);
        _panels = panelTemplates.DeepClone()!.AsArray();
        _selectedSchemaId = ConfigurableDataSchemaIds.SchemaId;
        _selectedRecordId = ConfigurableDataSchemaIds.WorkbenchRecordId;
        _selectedPanelId = ConfigurableDataSchemaIds.PanelMixed;
        _selectedPinName = "x";
        _selectedBindingPath = "position.x";
        _layer = DataSchemaAuthoringLayer.Record;
        Revalidate();
        _status = "已从启动资产装入作者草稿。";
    }

    public void SetLayer(DataSchemaAuthoringLayer layer)
    {
        _layer = layer;
        _status = $"切换到 {layer} 层。";
    }

    public void SelectSchema(string schemaId)
    {
        _selectedSchemaId = schemaId;
        _status = $"选中 schema {schemaId}";
    }

    public void SelectRecord(string recordId)
    {
        _selectedRecordId = recordId;
        foreach (JsonNode? node in _records)
        {
            if (node is JsonObject record &&
                string.Equals(record["id"]?.GetValue<string>(), recordId, StringComparison.Ordinal))
            {
                string? schemaId = record["schema"]?.GetValue<string>();
                if (!string.IsNullOrWhiteSpace(schemaId))
                {
                    _selectedSchemaId = schemaId;
                }

                break;
            }
        }

        _status = $"选中 record {recordId}";
    }

    public IReadOnlyList<string> EnumerateSchemaIds()
    {
        var ids = new List<string>();
        foreach (JsonNode? node in _schemas)
        {
            if (node is JsonObject schema &&
                schema["id"] is JsonValue idValue &&
                idValue.TryGetValue<string>(out string? id) &&
                !string.IsNullOrWhiteSpace(id))
            {
                ids.Add(id);
            }
        }

        return ids;
    }

    public IReadOnlyList<string> EnumerateRecordIds()
    {
        var ids = new List<string>();
        foreach (JsonNode? node in _records)
        {
            if (node is JsonObject record &&
                record["id"] is JsonValue idValue &&
                idValue.TryGetValue<string>(out string? id) &&
                !string.IsNullOrWhiteSpace(id))
            {
                ids.Add(id);
            }
        }

        return ids;
    }

    public void ClearDraftCatalog()
    {
        _schemas = new JsonArray();
        _records = new JsonArray();
        _selectedSchemaId = string.Empty;
        _selectedRecordId = string.Empty;
        Revalidate();
        _status = "已清空作者草稿目录，可从零定义 schema。";
    }

    public void CreateStructSchema(string schemaId)
    {
        if (string.IsNullOrWhiteSpace(schemaId))
        {
            _status = "struct id 不能为空。";
            return;
        }

        if (FindSchema(schemaId) != null)
        {
            _status = $"schema {schemaId} 已存在。";
            return;
        }

        _schemas.Add(new JsonObject
        {
            ["id"] = schemaId.Trim(),
            ["kind"] = "struct",
            ["fields"] = new JsonArray(),
        });
        _selectedSchemaId = schemaId.Trim();
        Revalidate();
        _status = _canSave || string.IsNullOrEmpty(_firstError)
            ? $"已新建 struct {schemaId}"
            : $"已新建 struct，预检：{_firstError}";
    }

    public void CreateEnumSchema(string schemaId)
    {
        if (string.IsNullOrWhiteSpace(schemaId))
        {
            _status = "enum id 不能为空。";
            return;
        }

        if (FindSchema(schemaId) != null)
        {
            _status = $"schema {schemaId} 已存在。";
            return;
        }

        _schemas.Add(new JsonObject
        {
            ["id"] = schemaId.Trim(),
            ["kind"] = "enum",
            ["values"] = new JsonArray(),
        });
        _selectedSchemaId = schemaId.Trim();
        Revalidate();
        _status = $"已新建 enum {schemaId}";
    }

    public void CreateRecord(string recordId, string schemaId)
    {
        if (string.IsNullOrWhiteSpace(recordId) || string.IsNullOrWhiteSpace(schemaId))
        {
            _status = "record/schema id 不能为空。";
            return;
        }

        JsonObject? schema = FindSchema(schemaId);
        if (schema == null || !string.Equals(schema["kind"]?.GetValue<string>(), "struct", StringComparison.Ordinal))
        {
            _status = $"找不到 struct schema {schemaId}";
            return;
        }

        foreach (JsonNode? node in _records)
        {
            if (node is JsonObject existing &&
                string.Equals(existing["id"]?.GetValue<string>(), recordId, StringComparison.Ordinal))
            {
                _status = $"record {recordId} 已存在。";
                return;
            }
        }

        var value = new JsonObject();
        if (schema["fields"] is JsonArray fields)
        {
            foreach (JsonNode? fieldNode in fields)
            {
                if (fieldNode is not JsonObject field)
                {
                    continue;
                }

                string name = field["name"]?.GetValue<string>() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                value[name] = DefaultValueForFieldType(field["type"]);
            }
        }

        _records.Add(new JsonObject
        {
            ["id"] = recordId.Trim(),
            ["schema"] = schemaId.Trim(),
            ["value"] = value,
        });
        _selectedRecordId = recordId.Trim();
        _selectedSchemaId = schemaId.Trim();
        Revalidate();
        _status = _canSave
            ? $"已创建 record {recordId}"
            : $"已创建 record，校验：{_firstError}";
    }

    public void BuildScoutFromScratch()
    {
        ClearDraftCatalog();
        CreateStructSchema("point");
        _newFieldName = "x";
        _newFieldType = "float";
        _newFieldRequired = true;
        AddFieldToSelectedSchema();
        _newFieldName = "y";
        AddFieldToSelectedSchema();

        CreateEnumSchema("rarity");
        AddEnumMember("rarity", "Common", 1);
        AddEnumMember("rarity", "Rare", 5);

        CreateStructSchema("unit");
        _newFieldName = "name";
        _newFieldType = "string";
        AddFieldToSelectedSchema();
        _newFieldName = "position";
        _newFieldType = "struct:point";
        AddFieldToSelectedSchema();
        _newFieldName = "tags";
        _newFieldType = "array:string";
        AddFieldToSelectedSchema();
        _newFieldName = "rarity";
        _newFieldType = "enum:rarity";
        AddFieldToSelectedSchema();
        _newFieldName = "focusTarget";
        _newFieldType = "entityRef";
        _newFieldRequired = false;
        AddFieldToSelectedSchema();

        CreateRecord("unit.scout", "unit");
        JsonObject? scout = GetSelectedRecordValue();
        if (scout != null)
        {
            scout["name"] = "Scout";
            scout["position"] = new JsonObject { ["x"] = 12d, ["y"] = 4d };
            scout["tags"] = new JsonArray("light", "recon");
            scout["rarity"] = "Common";
            scout["focusTarget"] = "WorkbenchOwner";
        }

        CreateRecord(ConfigurableDataSchemaIds.WorkbenchRecordId, "unit");
        SelectRecord(ConfigurableDataSchemaIds.WorkbenchRecordId);
        JsonObject? workbench = GetSelectedRecordValue();
        if (workbench != null && scout != null)
        {
            foreach (KeyValuePair<string, JsonNode?> pair in scout)
            {
                workbench[pair.Key] = pair.Value?.DeepClone();
            }
        }

        _selectedPinName = "x";
        _selectedBindingPath = "position.x";
        ApplyBindingToSelectedPin();
        Revalidate();
        _status = _canSave
            ? "已从零定义 point/rarity/unit 与 unit.scout。"
            : $"从零定义完成，仍有预检：{_firstError}";
    }

    public void SetSelectedRecordPathString(string path, string value)
    {
        JsonObject? draft = GetSelectedRecordValue();
        if (draft == null)
        {
            return;
        }

        if (!TryGetOrCreateObjectPath(draft, path, out JsonObject parent, out string leaf))
        {
            _status = $"路径不存在：{path}";
            return;
        }

        parent[leaf] = value;
        Revalidate();
        _status = _canSave ? $"已设置 {path}" : $"设置后校验失败：{_firstError}";
    }

    public void SetSelectedRecordPathNumber(string path, double value)
    {
        JsonObject? draft = GetSelectedRecordValue();
        if (draft == null)
        {
            return;
        }

        if (!TryGetOrCreateObjectPath(draft, path, out JsonObject parent, out string leaf))
        {
            _status = $"路径不存在：{path}";
            return;
        }

        parent[leaf] = value;
        Revalidate();
        _status = _canSave ? $"已设置 {path}={value}" : $"设置后校验失败：{_firstError}";
    }

    public void SetSelectedRecordPathBool(string path, bool value)
    {
        JsonObject? draft = GetSelectedRecordValue();
        if (draft == null)
        {
            return;
        }

        if (!TryGetOrCreateObjectPath(draft, path, out JsonObject parent, out string leaf))
        {
            _status = $"路径不存在：{path}";
            return;
        }

        parent[leaf] = value;
        Revalidate();
    }

    public void SetSelectedRecordEnum(string path, string enumName)
    {
        SetSelectedRecordPathString(path, enumName);
    }

    public void SetSelectedRecordEntityRef(string path, string entityName)
    {
        SetSelectedRecordPathString(path, entityName);
    }

    public void AddArrayItem(string path, string itemValue)
    {
        JsonObject? draft = GetSelectedRecordValue();
        if (draft == null)
        {
            return;
        }

        if (!TryGetObjectPath(draft, path, out JsonObject parent, out string leaf))
        {
            _status = $"数组路径不存在：{path}";
            return;
        }

        JsonArray array = parent[leaf] as JsonArray ?? new JsonArray();
        parent[leaf] = array;
        array.Add(itemValue);
        Revalidate();
        _status = $"已向 {path} 追加项。";
    }

    public void RemoveArrayItemAt(string path, int index)
    {
        JsonObject? draft = GetSelectedRecordValue();
        if (draft == null || !TryGetObjectPath(draft, path, out JsonObject parent, out string leaf))
        {
            return;
        }

        if (parent[leaf] is not JsonArray array || index < 0 || index >= array.Count)
        {
            _status = $"无法删除 {path}[{index}]";
            return;
        }

        array.RemoveAt(index);
        Revalidate();
    }

    public void MoveArrayItem(string path, int index, int delta)
    {
        JsonObject? draft = GetSelectedRecordValue();
        if (draft == null || !TryGetObjectPath(draft, path, out JsonObject parent, out string leaf))
        {
            return;
        }

        if (parent[leaf] is not JsonArray array)
        {
            return;
        }

        int target = index + delta;
        if (index < 0 || index >= array.Count || target < 0 || target >= array.Count)
        {
            return;
        }

        JsonNode? moving = array[index];
        array.RemoveAt(index);
        array.Insert(target, moving);
        Revalidate();
    }

    public IReadOnlyList<AuthoringFormField> EnumerateFormFields()
    {
        var fields = new List<AuthoringFormField>();
        JsonObject? schema = FindSchema(_selectedSchemaId);
        JsonObject? value = GetSelectedRecordValue();
        if (schema == null || value == null)
        {
            return fields;
        }

        CollectFormFields(schema, value, prefix: string.Empty, fields, depth: 0);
        return fields;
    }

    public void CycleNewFieldName()
    {
        _newFieldName = _newFieldName switch
        {
            "notes" => "speed",
            "speed" => "faction",
            "faction" => "focusTarget",
            _ => "notes",
        };
    }

    public void CycleNewFieldType()
    {
        _newFieldType = _newFieldType switch
        {
            "string" => "float",
            "float" => "bool",
            "bool" => "int",
            "int" => "entityRef",
            "entityRef" => "enum:rarity",
            "enum:rarity" => "struct:point",
            "struct:point" => "array:string",
            _ => "string",
        };
    }

    public void ToggleNewFieldRequired()
    {
        _newFieldRequired = !_newFieldRequired;
    }

    public void AddFieldToSelectedSchema()
    {
        JsonObject? schema = FindSchema(_selectedSchemaId);
        if (schema == null)
        {
            _status = $"找不到 schema {_selectedSchemaId}";
            Revalidate();
            return;
        }

        if (!string.Equals(schema["kind"]?.GetValue<string>(), "struct", StringComparison.Ordinal))
        {
            _status = "只能给 struct 加字段。";
            return;
        }

        JsonArray fields = schema["fields"] as JsonArray ?? new JsonArray();
        schema["fields"] = fields;
        foreach (JsonNode? node in fields)
        {
            if (node is JsonObject field &&
                string.Equals(field["name"]?.GetValue<string>(), _newFieldName, StringComparison.Ordinal))
            {
                _status = $"字段 {_newFieldName} 已存在。";
                return;
            }
        }

        fields.Add(new JsonObject
        {
            ["name"] = _newFieldName,
            ["type"] = BuildTypeNode(_newFieldType),
            ["required"] = _newFieldRequired,
        });
        EnsureRecordHasNewField(_newFieldName, _newFieldType);
        Revalidate();
        _status = _canSave
            ? $"已添加字段 {_newFieldName}（{_newFieldType}）。"
            : $"已添加字段，但校验失败：{_firstError}";
    }

    public void AddEnumMember(string schemaId, string memberName, int value)
    {
        JsonObject? schema = FindSchema(schemaId);
        if (schema == null || !string.Equals(schema["kind"]?.GetValue<string>(), "enum", StringComparison.Ordinal))
        {
            _status = $"找不到 enum {schemaId}";
            return;
        }

        JsonArray values = schema["values"] as JsonArray ?? new JsonArray();
        schema["values"] = values;
        values.Add(new JsonObject { ["name"] = memberName, ["value"] = value });
        Revalidate();
        _status = _canSave ? $"已添加 enum 成员 {memberName}={value}" : $"enum 添加后校验失败：{_firstError}";
    }

    public JsonObject? GetSelectedRecordValue()
    {
        foreach (JsonNode? node in _records)
        {
            if (node is JsonObject record &&
                string.Equals(record["id"]?.GetValue<string>(), _selectedRecordId, StringComparison.Ordinal))
            {
                return record["value"] as JsonObject;
            }
        }

        return null;
    }

    public void SetSelectedRecordString(string field, string value)
    {
        JsonObject? draft = GetSelectedRecordValue();
        if (draft == null)
        {
            return;
        }

        draft[field] = value;
        Revalidate();
    }

    public void NudgeSelectedRecordFloat(string path, double delta)
    {
        JsonObject? draft = GetSelectedRecordValue();
        if (draft == null)
        {
            return;
        }

        if (!TryGetObjectPath(draft, path, out JsonObject parent, out string leaf))
        {
            _status = $"路径不存在：{path}";
            return;
        }

        double current = 0;
        if (parent[leaf] is JsonValue number)
        {
            if (number.TryGetValue<double>(out double raw))
            {
                current = raw;
            }
            else if (number.TryGetValue<float>(out float asFloat))
            {
                current = asFloat;
            }
        }

        parent[leaf] = current + delta;
        Revalidate();
        _status = _canSave ? $"已更新 {path}" : $"更新后校验失败：{_firstError}";
    }

    public string[] EnumerateEnumNames(string schemaId)
    {
        JsonObject? schema = FindSchema(schemaId);
        if (schema == null || schema["values"] is not JsonArray values)
        {
            return Array.Empty<string>();
        }

        var names = new List<string>();
        foreach (JsonNode? node in values)
        {
            if (node is JsonObject value &&
                value["name"] is JsonValue nameValue &&
                nameValue.TryGetValue<string>(out string? name) &&
                !string.IsNullOrWhiteSpace(name))
            {
                names.Add(name);
            }
        }

        return names.ToArray();
    }

    public void CycleSelectedRecordEnum(string field, params string[] names)
    {
        CycleSelectedRecordEnumAtPath(field, names);
    }

    public void CycleSelectedRecordEnumAtPath(string path, params string[] names)
    {
        JsonObject? draft = GetSelectedRecordValue();
        if (draft == null || names.Length == 0)
        {
            return;
        }

        if (!TryGetObjectPath(draft, path, out JsonObject parent, out string leaf))
        {
            _status = $"路径不存在：{path}";
            return;
        }

        string current = parent[leaf] is JsonValue value && value.TryGetValue<string>(out string? text)
            ? text ?? names[0]
            : names[0];
        int index = Array.IndexOf(names, current);
        parent[leaf] = names[(index + 1 + names.Length) % names.Length];
        Revalidate();
    }

    public void RemoveLastArrayItem(string path)
    {
        JsonObject? draft = GetSelectedRecordValue();
        if (draft == null || !TryGetObjectPath(draft, path, out JsonObject parent, out string leaf))
        {
            return;
        }

        if (parent[leaf] is not JsonArray array || array.Count == 0)
        {
            return;
        }

        array.RemoveAt(array.Count - 1);
        Revalidate();
    }

    public void AddTag(string tag)
    {
        JsonObject? draft = GetSelectedRecordValue();
        if (draft == null)
        {
            return;
        }

        JsonArray tags = draft["tags"] as JsonArray ?? new JsonArray();
        draft["tags"] = tags;
        tags.Add(tag);
        Revalidate();
    }

    public void RemoveLastTag()
    {
        JsonObject? draft = GetSelectedRecordValue();
        if (draft?["tags"] is not JsonArray tags || tags.Count == 0)
        {
            return;
        }

        tags.RemoveAt(tags.Count - 1);
        Revalidate();
    }

    public IReadOnlyList<string> EnumerateBindingPaths(string schemaId)
    {
        var paths = new List<string>();
        JsonObject? schema = FindSchema(schemaId);
        if (schema == null)
        {
            return paths;
        }

        CollectPaths(schema, GetSelectedRecordValue(), prefix: string.Empty, paths, depth: 0);
        return paths;
    }

    public bool IsBindingPathAllowed(string schemaId, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        IReadOnlyList<string> allowed = EnumerateBindingPaths(schemaId);
        for (int i = 0; i < allowed.Count; i++)
        {
            if (string.Equals(allowed[i], path, StringComparison.Ordinal))
            {
                return true;
            }
        }

        // Accept schema-level array root even when empty (e.g. tags).
        string schemaPath = StripArrayIndices(path);
        JsonObject? schema = FindSchema(schemaId);
        if (schema == null)
        {
            return false;
        }

        var schemaOnly = new List<string>();
        CollectPaths(schema, recordValue: null, prefix: string.Empty, schemaOnly, depth: 0);
        for (int i = 0; i < schemaOnly.Count; i++)
        {
            if (string.Equals(schemaOnly[i], schemaPath, StringComparison.Ordinal) ||
                string.Equals(schemaOnly[i], path, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    public void SelectBindingPath(string path)
    {
        _selectedBindingPath = path;
        ApplyBindingToSelectedPin();
    }

    public void SelectPin(string pinName)
    {
        _selectedPinName = pinName;
        _status = $"选中 pin {pinName}";
    }

    public void SetSelectedPinSource(string source)
    {
        JsonObject? pin = FindPin(_selectedPanelId, _selectedPinName);
        if (pin == null)
        {
            _status = $"找不到 pin {_selectedPinName}";
            return;
        }

        if (string.Equals(source, "data", StringComparison.Ordinal))
        {
            pin["source"] = "data";
            pin.Remove("key");
            pin["record"] = _selectedRecordId;
            pin["path"] = _selectedBindingPath;
        }
        else
        {
            pin["source"] = "graph";
            pin.Remove("record");
            pin.Remove("path");
            if (!pin.ContainsKey("key"))
            {
                pin["key"] = "dataschema.panel.score";
            }
        }

        Revalidate();
        _status = $"pin {_selectedPinName} source={source}";
    }

    public DataSchemaModAssetWriteResult Save(DataSchemaModAssetWriter writer)
    {
        Revalidate();
        if (!_canSave)
        {
            _status = $"保存已禁用：{_firstError}";
            return new DataSchemaModAssetWriteResult(false, Array.Empty<string>(), new[] { _firstError });
        }

        DataSchemaModAssetWriteResult result = writer.Save(_saveTargetRoot, _schemas, _records, _panels);
        _status = result.Succeeded
            ? $"已写回 {_saveTargetRoot}：{string.Join(", ", result.WrittenRelativePaths)}"
            : $"保存失败：{string.Join("; ", result.Diagnostics)}";
        _canSave = result.Succeeded && string.IsNullOrEmpty(_firstError);
        return result;
    }

    public void PublishWorkbenchRecord(DataSchemaProjectionSession session)
    {
        JsonObject? value = GetSelectedRecordValue();
        if (value == null)
        {
            return;
        }

        string schemaId = ConfigurableDataSchemaIds.SchemaId;
        foreach (JsonNode? node in _records)
        {
            if (node is JsonObject record &&
                string.Equals(record["id"]?.GetValue<string>(), _selectedRecordId, StringComparison.Ordinal))
            {
                schemaId = record["schema"]?.GetValue<string>() ?? schemaId;
                break;
            }
        }

        if (!session.TryPublishRecordDraft(
                ConfigurableDataSchemaIds.WorkbenchRecordId,
                schemaId,
                CloneAsWorkbenchValue(value),
                out string error))
        {
            _firstError = error;
            _canSave = false;
            _status = $"预览失败：{error}";
        }
    }

    private JsonObject CloneAsWorkbenchValue(JsonObject value)
    {
        // Workbench panel always binds unit.workbench; keep authoring record id separate.
        return value.DeepClone().AsObject();
    }

    private void ApplyBindingToSelectedPin()
    {
        JsonObject? pin = FindPin(_selectedPanelId, _selectedPinName);
        if (pin == null)
        {
            return;
        }

        pin["source"] = "data";
        pin.Remove("key");
        pin["record"] = ConfigurableDataSchemaIds.WorkbenchRecordId;
        pin["path"] = _selectedBindingPath;
        Revalidate();
        _status = $"已绑定 {_selectedPinName} → {_selectedBindingPath}";
    }

    private void EnsureRecordHasNewField(string fieldName, string typeToken)
    {
        foreach (JsonNode? node in _records)
        {
            if (node is not JsonObject record)
            {
                continue;
            }

            if (!string.Equals(record["schema"]?.GetValue<string>(), _selectedSchemaId, StringComparison.Ordinal))
            {
                continue;
            }

            if (record["value"] is not JsonObject value || value.ContainsKey(fieldName))
            {
                continue;
            }

            value[fieldName] = DefaultValueForType(typeToken);
        }
    }

    private static JsonNode DefaultValueForType(string typeToken)
    {
        if (typeToken.StartsWith("array:", StringComparison.Ordinal))
        {
            return new JsonArray();
        }

        if (typeToken.StartsWith("struct:point", StringComparison.Ordinal))
        {
            return new JsonObject { ["x"] = 0d, ["y"] = 0d };
        }

        if (typeToken.StartsWith("enum:", StringComparison.Ordinal))
        {
            return "Common";
        }

        return typeToken switch
        {
            "float" => 0d,
            "bool" => false,
            "int" => 0,
            "entityRef" => string.Empty,
            _ => string.Empty,
        };
    }

    private void CollectFormFields(
        JsonObject schema,
        JsonObject value,
        string prefix,
        List<AuthoringFormField> fields,
        int depth)
    {
        if (depth > 8 || schema["fields"] is not JsonArray schemaFields)
        {
            return;
        }

        foreach (JsonNode? node in schemaFields)
        {
            if (node is not JsonObject field)
            {
                continue;
            }

            string name = field["name"]?.GetValue<string>() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            string path = string.IsNullOrEmpty(prefix) ? name : $"{prefix}.{name}";
            bool required = field["required"]?.GetValue<bool>() ?? false;
            JsonNode? typeNode = field["type"];
            string kind = DescribeTypeKind(typeNode, out string typeRef);
            JsonNode? fieldValue = value[name];

            if (string.Equals(kind, "struct", StringComparison.Ordinal) &&
                fieldValue is JsonObject nestedValue &&
                !string.IsNullOrWhiteSpace(typeRef))
            {
                JsonObject? nestedSchema = FindSchema(typeRef);
                if (nestedSchema != null)
                {
                    CollectFormFields(nestedSchema, nestedValue, path, fields, depth + 1);
                }

                continue;
            }

            int arrayLength = fieldValue is JsonArray array ? array.Count : 0;
            fields.Add(new AuthoringFormField(
                path,
                kind,
                FormatFieldValue(fieldValue),
                typeRef,
                required,
                arrayLength));
        }
    }

    private void CollectPaths(
        JsonObject schema,
        JsonObject? recordValue,
        string prefix,
        List<string> paths,
        int depth)
    {
        if (depth > 8 || schema["fields"] is not JsonArray fields)
        {
            return;
        }

        foreach (JsonNode? node in fields)
        {
            if (node is not JsonObject field)
            {
                continue;
            }

            string name = field["name"]?.GetValue<string>() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            string path = string.IsNullOrEmpty(prefix) ? name : $"{prefix}.{name}";
            paths.Add(path);
            JsonNode? typeNode = field["type"];
            JsonNode? valueNode = recordValue?[name];

            if (typeNode is JsonObject typeObject &&
                string.Equals(typeObject["kind"]?.GetValue<string>(), "struct", StringComparison.Ordinal))
            {
                string? typeRef = typeObject["ref"]?.GetValue<string>();
                if (!string.IsNullOrWhiteSpace(typeRef))
                {
                    JsonObject? nested = FindSchema(typeRef);
                    JsonObject? nestedValue = valueNode as JsonObject;
                    if (nested != null)
                    {
                        CollectPaths(nested, nestedValue, path, paths, depth + 1);
                    }
                }
            }
            else if (IsArrayType(typeNode) && valueNode is JsonArray array)
            {
                for (int i = 0; i < array.Count; i++)
                {
                    paths.Add($"{path}[{i}]");
                }
            }
        }
    }

    private static bool IsArrayType(JsonNode? typeNode)
    {
        if (typeNode is JsonObject typeObject)
        {
            return string.Equals(typeObject["kind"]?.GetValue<string>(), "array", StringComparison.Ordinal);
        }

        return typeNode is JsonValue text &&
               text.TryGetValue<string>(out string? token) &&
               token != null &&
               token.StartsWith("array:", StringComparison.Ordinal);
    }

    private static string DescribeTypeKind(JsonNode? typeNode, out string typeRef)
    {
        typeRef = string.Empty;
        if (typeNode is JsonObject typeObject)
        {
            string kind = typeObject["kind"]?.GetValue<string>() ?? "string";
            typeRef = typeObject["ref"]?.GetValue<string>()
                ?? typeObject["items"]?.ToJsonString()
                ?? string.Empty;
            return kind;
        }

        if (typeNode is JsonValue value && value.TryGetValue<string>(out string? token) && token != null)
        {
            if (token.StartsWith("enum:", StringComparison.Ordinal))
            {
                typeRef = token["enum:".Length..];
                return "enum";
            }

            if (token.StartsWith("struct:", StringComparison.Ordinal))
            {
                typeRef = token["struct:".Length..];
                return "struct";
            }

            if (token.StartsWith("array:", StringComparison.Ordinal))
            {
                typeRef = token["array:".Length..];
                return "array";
            }

            return token;
        }

        return "string";
    }

    private static string FormatFieldValue(JsonNode? value)
    {
        if (value == null)
        {
            return "(missing)";
        }

        if (value is JsonArray array)
        {
            return $"[{array.Count}] {array.ToJsonString()}";
        }

        return value.ToJsonString();
    }

    private static JsonNode? ResolvePathValue(JsonObject root, string path)
    {
        string[] parts = path.Split('.', StringSplitOptions.RemoveEmptyEntries);
        JsonNode? current = root;
        for (int i = 0; i < parts.Length; i++)
        {
            if (current is not JsonObject obj)
            {
                return null;
            }

            string part = parts[i];
            int bracket = part.IndexOf('[', StringComparison.Ordinal);
            if (bracket >= 0)
            {
                string name = part[..bracket];
                if (!obj.TryGetPropertyValue(name, out JsonNode? arrNode) || arrNode is not JsonArray array)
                {
                    return null;
                }

                int end = part.IndexOf(']', bracket);
                if (end < 0 ||
                    !int.TryParse(part[(bracket + 1)..end], NumberStyles.Integer, CultureInfo.InvariantCulture, out int index) ||
                    index < 0 ||
                    index >= array.Count)
                {
                    return null;
                }

                current = array[index];
            }
            else if (!obj.TryGetPropertyValue(part, out current))
            {
                return null;
            }
        }

        return current;
    }

    private static string StripArrayIndices(string path)
    {
        if (string.IsNullOrEmpty(path) || path.IndexOf('[', StringComparison.Ordinal) < 0)
        {
            return path;
        }

        var builder = new System.Text.StringBuilder(path.Length);
        for (int i = 0; i < path.Length; i++)
        {
            if (path[i] == '[')
            {
                while (i < path.Length && path[i] != ']')
                {
                    i++;
                }

                continue;
            }

            builder.Append(path[i]);
        }

        return builder.ToString();
    }

    private bool TryGetOrCreateObjectPath(JsonObject root, string path, out JsonObject parent, out string leaf)
    {
        parent = root;
        leaf = path;
        string[] parts = path.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return false;
        }

        for (int i = 0; i < parts.Length - 1; i++)
        {
            string part = parts[i];
            if (parent[part] is JsonObject next)
            {
                parent = next;
                continue;
            }

            var created = new JsonObject();
            parent[part] = created;
            parent = created;
        }

        leaf = parts[^1];
        if (leaf.IndexOf('[', StringComparison.Ordinal) >= 0)
        {
            return false;
        }

        return true;
    }

    private static JsonNode DefaultValueForFieldType(JsonNode? typeNode)
    {
        string kind = DescribeTypeKind(typeNode, out string typeRef);
        return kind switch
        {
            "float" => 0d,
            "int" => 0,
            "bool" => false,
            "entityRef" => string.Empty,
            "enum" => string.Empty,
            "array" => new JsonArray(),
            "struct" when !string.IsNullOrWhiteSpace(typeRef) => new JsonObject(),
            _ => string.Empty,
        };
    }

    private void CollectPaths(JsonObject schema, string prefix, List<string> paths, int depth)
    {
        CollectPaths(schema, recordValue: null, prefix, paths, depth);
    }

    private JsonObject? FindSchema(string id)
    {
        foreach (JsonNode? node in _schemas)
        {
            if (node is JsonObject schema &&
                string.Equals(schema["id"]?.GetValue<string>(), id, StringComparison.Ordinal))
            {
                return schema;
            }
        }

        return null;
    }

    private JsonObject? FindPin(string panelId, string pinName)
    {
        foreach (JsonNode? node in _panels)
        {
            if (node is not JsonObject panel ||
                !string.Equals(panel["id"]?.GetValue<string>(), panelId, StringComparison.Ordinal))
            {
                continue;
            }

            if (panel["pins"] is not JsonArray pins)
            {
                return null;
            }

            foreach (JsonNode? pinNode in pins)
            {
                if (pinNode is JsonObject pin &&
                    string.Equals(pin["name"]?.GetValue<string>(), pinName, StringComparison.Ordinal))
                {
                    return pin;
                }
            }
        }

        return null;
    }

    private void Revalidate()
    {
        var writer = new DataSchemaModAssetWriter();
        DataSchemaModAssetWritePlan plan = writer.Preview(_saveTargetRoot, _schemas, _records, _panels);
        var diagnostics = new List<string>(plan.Diagnostics);
        ValidateDataPinPaths(diagnostics);
        _canSave = diagnostics.Count == 0;
        _firstError = diagnostics.Count > 0 ? diagnostics[0] : string.Empty;
    }

    private void ValidateDataPinPaths(List<string> diagnostics)
    {
        foreach (JsonNode? panelNode in _panels)
        {
            if (panelNode is not JsonObject panel || panel["pins"] is not JsonArray pins)
            {
                continue;
            }

            string panelId = panel["id"]?.GetValue<string>() ?? "(panel)";
            foreach (JsonNode? pinNode in pins)
            {
                if (pinNode is not JsonObject pin)
                {
                    continue;
                }

                if (!string.Equals(pin["source"]?.GetValue<string>(), "data", StringComparison.Ordinal))
                {
                    continue;
                }

                string pinName = pin["name"]?.GetValue<string>() ?? "(pin)";
                string path = pin["path"]?.GetValue<string>() ?? string.Empty;
                string recordId = pin["record"]?.GetValue<string>() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(path))
                {
                    diagnostics.Add($"Panel '{panelId}' pin '{pinName}' data path is empty.");
                    continue;
                }

                JsonObject? record = FindRecord(recordId);
                if (record == null)
                {
                    diagnostics.Add($"Panel '{panelId}' pin '{pinName}' references unknown record '{recordId}'.");
                    continue;
                }

                string schemaId = record["schema"]?.GetValue<string>() ?? _selectedSchemaId;
                if (!IsBindingPathAllowed(schemaId, path))
                {
                    diagnostics.Add($"Panel '{panelId}' pin '{pinName}' unknown path '{path}'.");
                }
            }
        }
    }

    private JsonObject? FindRecord(string id)
    {
        foreach (JsonNode? node in _records)
        {
            if (node is JsonObject record &&
                string.Equals(record["id"]?.GetValue<string>(), id, StringComparison.Ordinal))
            {
                return record;
            }
        }

        return null;
    }

    private static bool TryGetObjectPath(JsonObject root, string path, out JsonObject parent, out string leaf)
    {
        parent = root;
        leaf = path;
        string[] parts = path.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return false;
        }

        for (int i = 0; i < parts.Length - 1; i++)
        {
            if (parent[parts[i]] is not JsonObject next)
            {
                return false;
            }

            parent = next;
        }

        leaf = parts[^1];
        return true;
    }

    private static JsonNode BuildTypeNode(string typeToken)
    {
        if (typeToken.StartsWith("enum:", StringComparison.Ordinal))
        {
            return new JsonObject { ["kind"] = "enum", ["ref"] = typeToken["enum:".Length..] };
        }

        if (typeToken.StartsWith("struct:", StringComparison.Ordinal))
        {
            return new JsonObject { ["kind"] = "struct", ["ref"] = typeToken["struct:".Length..] };
        }

        if (typeToken.StartsWith("array:", StringComparison.Ordinal))
        {
            return new JsonObject
            {
                ["kind"] = "array",
                ["items"] = BuildTypeNode(typeToken["array:".Length..]),
            };
        }

        return JsonValue.Create(typeToken)!;
    }

    private static JsonArray SerializeCatalog(DataSchemaCatalog catalog)
    {
        var array = new JsonArray();
        foreach (DataSchemaDefinition definition in catalog.Definitions)
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
            }
            else
            {
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
            }

            array.Add(obj);
        }

        return array;
    }

    private static JsonNode SerializeType(DataSchemaType type) => type.Kind switch
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

    private static JsonArray SerializeRecords(DataSchemaRegistry registry)
    {
        var array = new JsonArray();
        foreach (DataSchemaRecord record in registry.Records)
        {
            array.Add(new JsonObject
            {
                ["id"] = record.Id,
                ["schema"] = record.SchemaId,
                ["value"] = record.Value.DeepClone(),
            });
        }

        return array;
    }
}
