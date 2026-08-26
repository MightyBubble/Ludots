using System;
using System.Text.Json.Nodes;

namespace Ludots.Core.Config;

/// <summary>
/// Isolates panel data projection from the immutable startup registry.
/// Showcase and authoring tools publish validated drafts here; formal ConfigPipeline
/// loads remain untouched until an explicit asset write-back.
/// </summary>
public sealed class DataSchemaProjectionSession
{
	private readonly DataSchemaRegistry _startup;
	private DataSchemaRegistry _active;
	private uint _revision;

	public DataSchemaProjectionSession(DataSchemaRegistry startup)
	{
		_startup = startup ?? throw new ArgumentNullException(nameof(startup));
		_active = startup;
		_revision = startup.Revision;
	}

	public DataSchemaCatalog Catalog => _startup.Catalog;

	public DataSchemaRegistry Startup => _startup;

	public DataSchemaRegistry Active => _active;

	public uint Revision => _revision;

	public bool IsPreview => !ReferenceEquals(_active, _startup);

	public void UseStartup()
	{
		if (ReferenceEquals(_active, _startup))
		{
			return;
		}

		_active = _startup;
		_revision++;
	}

	public void UsePreview(DataSchemaRegistry preview)
	{
		_active = preview ?? throw new ArgumentNullException(nameof(preview));
		_revision++;
	}

	public bool TryGetNode(string recordId, string path, out JsonNode? node)
	{
		return _active.TryGetNode(recordId, path, out node);
	}

	/// <summary>
	/// Rebuilds a preview registry from startup records, replacing one record's value
	/// after schema validation. Invalid drafts leave the active registry unchanged.
	/// </summary>
	public bool TryPublishRecordDraft(string recordId, string schemaId, JsonObject value, out string error)
	{
		if (string.IsNullOrWhiteSpace(recordId))
		{
			throw new ArgumentException("Record id is required.", nameof(recordId));
		}

		if (string.IsNullOrWhiteSpace(schemaId))
		{
			throw new ArgumentException("Schema id is required.", nameof(schemaId));
		}

		ArgumentNullException.ThrowIfNull(value);

		var entries = new JsonArray();
		bool replaced = false;
		foreach (DataSchemaRecord record in _startup.Records)
		{
			if (string.Equals(record.Id, recordId, StringComparison.Ordinal))
			{
				entries.Add(new JsonObject
				{
					["id"] = recordId.Trim(),
					["schema"] = schemaId.Trim(),
					["value"] = value.DeepClone(),
				});
				replaced = true;
			}
			else
			{
				entries.Add(new JsonObject
				{
					["id"] = record.Id,
					["schema"] = record.SchemaId,
					["value"] = record.Value.DeepClone(),
				});
			}
		}

		if (!replaced)
		{
			entries.Add(new JsonObject
			{
				["id"] = recordId.Trim(),
				["schema"] = schemaId.Trim(),
				["value"] = value.DeepClone(),
			});
		}

		try
		{
			DataSchemaRegistry preview = DataSchemaRegistry.Load(_startup.Catalog, entries);
			UsePreview(preview);
			error = string.Empty;
			return true;
		}
		catch (Exception ex)
		{
			error = ex.Message;
			return false;
		}
	}
}
