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
        _status = $"选中 record {recordId}";
    }

    public void CycleNewFieldName()
    {
        _newFieldName = _newFieldName switch
        {
            "notes" => "speed",
            "speed" => "faction",
            _ => "notes",
        };
    }

    public void CycleNewFieldType()
    {
        _newFieldType = _newFieldType switch
        {
            "string" => "float",
            "float" => "bool",
            "bool" => "enum:rarity",
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
        JsonObject? draft = GetSelectedRecordValue();
        if (draft == null || names.Length == 0)
        {
            return;
        }

        string current = draft[field] is JsonValue value && value.TryGetValue<string>(out string? text)
            ? text ?? names[0]
            : names[0];
        int index = Array.IndexOf(names, current);
        draft[field] = names[(index + 1 + names.Length) % names.Length];
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

        CollectPaths(schema, prefix: string.Empty, paths, depth: 0);
        return paths;
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
            _ => string.Empty,
        };
    }

    private void CollectPaths(JsonObject schema, string prefix, List<string> paths, int depth)
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
            if (typeNode is JsonObject typeObject &&
                string.Equals(typeObject["kind"]?.GetValue<string>(), "struct", StringComparison.Ordinal))
            {
                string? typeRef = typeObject["ref"]?.GetValue<string>();
                if (!string.IsNullOrWhiteSpace(typeRef))
                {
                    JsonObject? nested = FindSchema(typeRef);
                    if (nested != null)
                    {
                        CollectPaths(nested, path, paths, depth + 1);
                    }
                }
            }
        }
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
        _canSave = plan.CanSave;
        _firstError = plan.Diagnostics.Count > 0 ? plan.Diagnostics[0] : string.Empty;
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
