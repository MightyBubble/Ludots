using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Ludots.Core.Config;

public enum DataSchemaTypeKind : byte
{
	String = 1,
	Int = 2,
	Float = 3,
	Bool = 4,
	EntityRef = 5,
	Struct = 6,
	Enum = 7,
	Array = 8,
}

public sealed record DataSchemaType(
	DataSchemaTypeKind Kind,
	string? Reference = null,
	DataSchemaType? ElementType = null);

public sealed record DataSchemaField(
	string Name,
	DataSchemaType Type,
	bool Required);

public sealed record DataSchemaEnumValue(string Name, int Value);

public enum DataSchemaDefinitionKind : byte
{
	Struct = 1,
	Enum = 2,
}

public sealed class DataSchemaDefinition
{
	public DataSchemaDefinition(
		string id,
		DataSchemaDefinitionKind kind,
		IReadOnlyList<DataSchemaField>? fields = null,
		IReadOnlyList<DataSchemaEnumValue>? enumValues = null)
	{
		if (string.IsNullOrWhiteSpace(id))
		{
			throw new ArgumentException("Schema id is required.", nameof(id));
		}

		Id = id.Trim();
		Kind = kind;
		Fields = fields ?? Array.Empty<DataSchemaField>();
		EnumValues = enumValues ?? Array.Empty<DataSchemaEnumValue>();
	}

	public string Id { get; }
	public DataSchemaDefinitionKind Kind { get; }
	public IReadOnlyList<DataSchemaField> Fields { get; }
	public IReadOnlyList<DataSchemaEnumValue> EnumValues { get; }

	public bool TryGetField(string name, out DataSchemaField field)
	{
		for (int i = 0; i < Fields.Count; i++)
		{
			if (string.Equals(Fields[i].Name, name, StringComparison.Ordinal))
			{
				field = Fields[i];
				return true;
			}
		}

		field = null!;
		return false;
	}

	public bool TryGetEnumValue(string name, out int value)
	{
		for (int i = 0; i < EnumValues.Count; i++)
		{
			if (string.Equals(EnumValues[i].Name, name, StringComparison.Ordinal))
			{
				value = EnumValues[i].Value;
				return true;
			}
		}

		value = 0;
		return false;
	}

	public bool IsKnownEnumValue(int value)
	{
		for (int i = 0; i < EnumValues.Count; i++)
		{
			if (EnumValues[i].Value == value)
			{
				return true;
			}
		}

		return false;
	}
}

public sealed class DataSchemaCatalog
{
	private readonly Dictionary<string, DataSchemaDefinition> _definitions;

	public DataSchemaCatalog(IEnumerable<DataSchemaDefinition> definitions)
	{
		ArgumentNullException.ThrowIfNull(definitions);
		_definitions = new Dictionary<string, DataSchemaDefinition>(StringComparer.Ordinal);
		foreach (DataSchemaDefinition definition in definitions)
		{
			if (!_definitions.TryAdd(definition.Id, definition))
			{
				throw new InvalidOperationException($"Duplicate data schema '{definition.Id}'.");
			}
		}

		ValidateReferences();
	}

	public static DataSchemaCatalog Empty { get; } = new(Array.Empty<DataSchemaDefinition>());

	public IReadOnlyCollection<DataSchemaDefinition> Definitions => _definitions.Values;

	public bool TryGet(string id, out DataSchemaDefinition definition) =>
		_definitions.TryGetValue(id, out definition!);

	public DataSchemaDefinition Require(string id)
	{
		if (!TryGet(id, out DataSchemaDefinition definition))
		{
			throw new InvalidOperationException($"Unknown data schema '{id}'.");
		}

		return definition;
	}

	public static DataSchemaCatalog Load(string json)
	{
		if (string.IsNullOrWhiteSpace(json))
		{
			throw new InvalidOperationException("Data schema JSON is empty.");
		}

		JsonNode root = JsonNode.Parse(json) ?? throw new InvalidOperationException("Data schema JSON parsed to null.");
		return Load(root as JsonArray ?? throw new InvalidOperationException("Data schema root must be an array."));
	}

	public static DataSchemaCatalog Load(JsonArray entries)
	{
		ArgumentNullException.ThrowIfNull(entries);
		var definitions = new List<DataSchemaDefinition>(entries.Count);
		for (int i = 0; i < entries.Count; i++)
		{
			if (entries[i] is not JsonObject entry)
			{
				throw new InvalidOperationException($"Data schema entry[{i}] must be an object.");
			}

			definitions.Add(ParseDefinition(entry, $"Data schema entry[{i}]"));
		}

		return new DataSchemaCatalog(definitions);
	}

	private static DataSchemaDefinition ParseDefinition(JsonObject entry, string context)
	{
		RejectUnknown(entry, context, "id", "kind", "fields", "values");
		string id = RequireString(entry, "id", context);
		string kindText = RequireString(entry, "kind", context);
		if (string.Equals(kindText, "struct", StringComparison.Ordinal))
		{
			if (entry["fields"] is not JsonArray fields)
			{
				throw new InvalidOperationException($"{context} '{id}' requires a fields array.");
			}

			var parsedFields = new List<DataSchemaField>(fields.Count);
			var names = new HashSet<string>(StringComparer.Ordinal);
			for (int i = 0; i < fields.Count; i++)
			{
				if (fields[i] is not JsonObject field)
				{
					throw new InvalidOperationException($"{context} '{id}' fields[{i}] must be an object.");
				}

				RejectUnknown(field, $"{context} '{id}' field[{i}]", "name", "type", "required");
				string fieldName = RequireString(field, "name", $"{context} '{id}' field[{i}]");
				if (!names.Add(fieldName))
				{
					throw new InvalidOperationException($"{context} '{id}' declares duplicate field '{fieldName}'.");
				}

				JsonNode typeNode = field["type"] ?? throw new InvalidOperationException($"{context} '{id}' field '{fieldName}' requires type.");
				bool required = field["required"]?.GetValue<bool>() ?? false;
				parsedFields.Add(new DataSchemaField(fieldName, ParseType(typeNode, $"{context} '{id}' field '{fieldName}'"), required));
			}

			return new DataSchemaDefinition(id, DataSchemaDefinitionKind.Struct, fields: parsedFields);
		}

		if (string.Equals(kindText, "enum", StringComparison.Ordinal))
		{
			if (entry["values"] is not JsonArray values || values.Count == 0)
			{
				throw new InvalidOperationException($"{context} '{id}' requires a non-empty values array.");
			}

			var parsedValues = new List<DataSchemaEnumValue>(values.Count);
			var names = new HashSet<string>(StringComparer.Ordinal);
			var numbers = new HashSet<int>();
			for (int i = 0; i < values.Count; i++)
			{
				if (values[i] is not JsonObject value)
				{
					throw new InvalidOperationException($"{context} '{id}' values[{i}] must be an object with name and value.");
				}

				RejectUnknown(value, $"{context} '{id}' values[{i}]", "name", "value");
				string name = RequireString(value, "name", $"{context} '{id}' values[{i}]");
				int number = value["value"]?.GetValue<int>() ?? throw new InvalidOperationException($"{context} '{id}' values[{i}] requires integer value.");
				if (!names.Add(name) || !numbers.Add(number))
				{
					throw new InvalidOperationException($"{context} '{id}' has duplicate enum name or value at '{name}'.");
				}

				parsedValues.Add(new DataSchemaEnumValue(name, number));
			}

			return new DataSchemaDefinition(id, DataSchemaDefinitionKind.Enum, enumValues: parsedValues);
		}

		throw new InvalidOperationException($"{context} '{id}' has unknown kind '{kindText}' (allowed: struct, enum).");
	}

	private static DataSchemaType ParseType(JsonNode node, string context)
	{
		if (node is JsonValue value && value.TryGetValue<string>(out string? text))
		{
			return text switch
			{
				"string" => new(DataSchemaTypeKind.String),
				"int" => new(DataSchemaTypeKind.Int),
				"float" => new(DataSchemaTypeKind.Float),
				"bool" => new(DataSchemaTypeKind.Bool),
				"entityRef" => new(DataSchemaTypeKind.EntityRef),
				_ => throw new InvalidOperationException($"{context} has unknown primitive type '{text}'."),
			};
		}

		if (node is not JsonObject type)
		{
			throw new InvalidOperationException($"{context} type must be a primitive string or type object.");
		}

		RejectUnknown(type, context, "kind", "ref", "items");
		string kind = RequireString(type, "kind", context);
		if (string.Equals(kind, "struct", StringComparison.Ordinal))
		{
			return new(DataSchemaTypeKind.Struct, RequireString(type, "ref", context));
		}

		if (string.Equals(kind, "enum", StringComparison.Ordinal))
		{
			return new(DataSchemaTypeKind.Enum, RequireString(type, "ref", context));
		}

		if (string.Equals(kind, "array", StringComparison.Ordinal))
		{
			JsonNode itemNode = type["items"] ?? throw new InvalidOperationException($"{context} array requires items.");
			return new(DataSchemaTypeKind.Array, ElementType: ParseType(itemNode, $"{context}.items"));
		}

		throw new InvalidOperationException($"{context} has unknown compound kind '{kind}'.");
	}

	private void ValidateReferences()
	{
		foreach (DataSchemaDefinition definition in _definitions.Values)
		{
			if (definition.Kind != DataSchemaDefinitionKind.Struct)
			{
				continue;
			}

			foreach (DataSchemaField field in definition.Fields)
			{
				ValidateTypeReference(field.Type, $"schema '{definition.Id}' field '{field.Name}'");
			}
		}

		var visiting = new HashSet<string>(StringComparer.Ordinal);
		var visited = new HashSet<string>(StringComparer.Ordinal);
		foreach (DataSchemaDefinition definition in _definitions.Values)
		{
			if (definition.Kind == DataSchemaDefinitionKind.Struct)
			{
				VisitStruct(definition.Id, visiting, visited);
			}
		}
	}

	private void ValidateTypeReference(DataSchemaType type, string context)
	{
		if (type.Kind is DataSchemaTypeKind.Struct or DataSchemaTypeKind.Enum)
		{
			DataSchemaDefinition target = Require(type.Reference!);
			DataSchemaDefinitionKind expected = type.Kind == DataSchemaTypeKind.Struct ? DataSchemaDefinitionKind.Struct : DataSchemaDefinitionKind.Enum;
			if (target.Kind != expected)
			{
				throw new InvalidOperationException($"{context} references '{target.Id}' as {type.Kind}, but it is {target.Kind}.");
			}
		}
		else if (type.Kind == DataSchemaTypeKind.Array)
		{
			ValidateTypeReference(type.ElementType!, $"{context} array item");
		}
	}

	private void VisitStruct(string id, HashSet<string> visiting, HashSet<string> visited)
	{
		if (visited.Contains(id)) return;
		if (!visiting.Add(id))
		{
			throw new InvalidOperationException($"Cyclic data schema reference detected at '{id}'.");
		}

		DataSchemaDefinition definition = Require(id);
		foreach (DataSchemaField field in definition.Fields)
		{
			VisitType(field.Type, visiting, visited);
		}

		visiting.Remove(id);
		visited.Add(id);
	}

	private void VisitType(DataSchemaType type, HashSet<string> visiting, HashSet<string> visited)
	{
		if (type.Kind == DataSchemaTypeKind.Struct) VisitStruct(type.Reference!, visiting, visited);
		else if (type.Kind == DataSchemaTypeKind.Array) VisitType(type.ElementType!, visiting, visited);
	}

	private static string RequireString(JsonObject obj, string name, string context)
	{
		if (obj[name] is JsonValue value && value.TryGetValue<string>(out string? text) && !string.IsNullOrWhiteSpace(text))
		{
			return text.Trim();
		}

		throw new InvalidOperationException($"{context} requires non-empty '{name}'.");
	}

	private static void RejectUnknown(JsonObject obj, string context, params string[] allowed)
	{
		var set = new HashSet<string>(allowed, StringComparer.Ordinal);
		foreach (KeyValuePair<string, JsonNode?> pair in obj)
		{
			if (!set.Contains(pair.Key))
			{
				throw new InvalidOperationException($"{context} has unknown field '{pair.Key}'.");
			}
		}
	}
}

public sealed record DataSchemaRecord(string Id, string SchemaId, JsonObject Value);

public sealed class DataSchemaRegistry
{
	private const int MaxDepth = 32;
	private const int MaxArrayLength = 4096;
	private readonly DataSchemaCatalog _catalog;
	private readonly Dictionary<string, DataSchemaRecord> _records = new(StringComparer.Ordinal);

	public DataSchemaRegistry(DataSchemaCatalog catalog)
	{
		_catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
	}

	public uint Revision { get; } = 1;

	public DataSchemaCatalog Catalog => _catalog;

	public IReadOnlyCollection<DataSchemaRecord> Records => _records.Values;

	public static DataSchemaRegistry Load(DataSchemaCatalog catalog, string json)
	{
		JsonNode root = JsonNode.Parse(json) ?? throw new InvalidOperationException("Data record JSON parsed to null.");
		return Load(catalog, root as JsonArray ?? throw new InvalidOperationException("Data record root must be an array."));
	}

	public static DataSchemaRegistry Load(DataSchemaCatalog catalog, JsonArray entries)
	{
		ArgumentNullException.ThrowIfNull(catalog);
		ArgumentNullException.ThrowIfNull(entries);
		var registry = new DataSchemaRegistry(catalog);
		for (int i = 0; i < entries.Count; i++)
		{
			if (entries[i] is not JsonObject entry)
			{
				throw new InvalidOperationException($"Data record entry[{i}] must be an object.");
			}

			foreach (KeyValuePair<string, JsonNode?> property in entry)
			{
				if (property.Key is not ("id" or "schema" or "value"))
				{
					throw new InvalidOperationException($"Data record entry[{i}] has unknown field '{property.Key}'.");
				}
			}

			string id = RequireString(entry, "id", $"Data record entry[{i}]");
			string schemaId = RequireString(entry, "schema", $"Data record '{id}'");
			DataSchemaDefinition schema = catalog.Require(schemaId);
			if (schema.Kind != DataSchemaDefinitionKind.Struct)
			{
				throw new InvalidOperationException($"Data record '{id}' must reference a struct schema, got '{schema.Kind}'.");
			}

			if (entry["value"] is not JsonObject value)
			{
				throw new InvalidOperationException($"Data record '{id}' requires an object value.");
			}

			ValidateValue(catalog, schema, value, $"record '{id}'", 0);
			if (!registry._records.TryAdd(id, new DataSchemaRecord(id, schemaId, (JsonObject)value.DeepClone())))
			{
				throw new InvalidOperationException($"Duplicate data record '{id}'.");
			}
		}

		return registry;
	}

	public bool TryGet(string id, out DataSchemaRecord record) => _records.TryGetValue(id, out record!);

	public DataSchemaRecord Require(string id)
	{
		if (!TryGet(id, out DataSchemaRecord record))
		{
			throw new InvalidOperationException($"Unknown data record '{id}'.");
		}

		return record;
	}

	public bool TryGetNode(string recordId, string path, out JsonNode? node)
	{
		node = null;
		if (!TryGet(recordId, out DataSchemaRecord record)) return false;
		JsonNode? current = record.Value;
		if (string.IsNullOrWhiteSpace(path))
		{
			node = current.DeepClone();
			return true;
		}

		string[] segments = path.Split('.', StringSplitOptions.RemoveEmptyEntries);
		for (int i = 0; i < segments.Length; i++)
		{
			if (current is not JsonObject obj || !obj.TryGetPropertyValue(segments[i], out current)) return false;
		}

		node = current?.DeepClone();
		return node != null;
	}

	private static void ValidateValue(DataSchemaCatalog catalog, DataSchemaDefinition schema, JsonNode value, string path, int depth)
	{
		if (depth > MaxDepth) throw new InvalidOperationException($"{path} exceeds maximum data nesting depth {MaxDepth}.");
		if (value is not JsonObject obj) throw new InvalidOperationException($"{path} must be an object.");

		foreach (DataSchemaField field in schema.Fields)
		{
			if (!obj.TryGetPropertyValue(field.Name, out JsonNode? fieldValue) || fieldValue == null)
			{
				if (field.Required) throw new InvalidOperationException($"{path} is missing required field '{field.Name}'.");
				continue;
			}

			ValidateType(catalog, field.Type, fieldValue, $"{path}.{field.Name}", depth + 1);
		}

		foreach (KeyValuePair<string, JsonNode?> property in obj)
		{
			if (!schema.TryGetField(property.Key, out _))
			{
				throw new InvalidOperationException($"{path} has unknown field '{property.Key}'.");
			}
		}
	}

	private static void ValidateType(DataSchemaCatalog catalog, DataSchemaType type, JsonNode value, string path, int depth)
	{
		if (depth > MaxDepth) throw new InvalidOperationException($"{path} exceeds maximum data nesting depth {MaxDepth}.");
		JsonValue? scalar = value as JsonValue;
		switch (type.Kind)
		{
			case DataSchemaTypeKind.String:
				if (scalar == null || !scalar.TryGetValue<string>(out _)) Fail(path, "string");
				break;
			case DataSchemaTypeKind.Int:
				if (scalar == null || !scalar.TryGetValue<int>(out _)) Fail(path, "integer");
				break;
			case DataSchemaTypeKind.Float:
				if (scalar == null || !scalar.TryGetValue<double>(out _)) Fail(path, "number");
				break;
			case DataSchemaTypeKind.Bool:
				if (scalar == null || !scalar.TryGetValue<bool>(out _)) Fail(path, "boolean");
				break;
			case DataSchemaTypeKind.EntityRef:
				if (scalar == null || !(scalar.TryGetValue<string>(out _) || scalar.TryGetValue<long>(out _))) Fail(path, "entity reference");
				break;
			case DataSchemaTypeKind.Struct:
				ValidateValue(catalog, catalog.Require(type.Reference!), value, path, depth);
				break;
			case DataSchemaTypeKind.Enum:
				ValidateEnum(catalog.Require(type.Reference!), value, path);
				break;
			case DataSchemaTypeKind.Array:
				JsonArray? array = value as JsonArray;
				if (array == null) Fail(path, "array");
				if (array.Count > MaxArrayLength) throw new InvalidOperationException($"{path} exceeds maximum array length {MaxArrayLength}.");
				for (int i = 0; i < array.Count; i++) ValidateType(catalog, type.ElementType!, array[i] ?? JsonValue.Create((string?)null)!, $"{path}[{i}]", depth + 1);
				break;
			default:
				throw new InvalidOperationException($"{path} has unsupported data type '{type.Kind}'.");
		}
	}

	private static void ValidateEnum(DataSchemaDefinition schema, JsonNode value, string path)
	{
		if (value is JsonValue text && text.TryGetValue<string>(out string? name))
		{
			if (!schema.TryGetEnumValue(name, out _)) throw new InvalidOperationException($"{path} has unknown enum member '{name}'.");
			return;
		}

		if (value is JsonValue number && number.TryGetValue<int>(out int numeric))
		{
			if (!schema.IsKnownEnumValue(numeric)) throw new InvalidOperationException($"{path} has unknown enum value {numeric.ToString(CultureInfo.InvariantCulture)}.");
			return;
		}

		Fail(path, "enum member name or integer value");
	}

	private static void Fail(string path, string expected) => throw new InvalidOperationException($"{path} requires {expected}.");

	private static string RequireString(JsonObject obj, string name, string context)
	{
		if (obj[name] is JsonValue value && value.TryGetValue<string>(out string? text) && !string.IsNullOrWhiteSpace(text)) return text.Trim();
		throw new InvalidOperationException($"{context} requires non-empty '{name}'.");
	}
}
