using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ludots.Core.UI.PanelProjection;

namespace Ludots.Core.Config;

public sealed class DataSchemaModAssetWritePlan
{
	public DataSchemaModAssetWritePlan(
		string modRootPath,
		IReadOnlyList<string> relativePaths,
		IReadOnlyList<string> diagnostics,
		bool canSave)
	{
		ModRootPath = modRootPath;
		RelativePaths = relativePaths;
		Diagnostics = diagnostics;
		CanSave = canSave;
	}

	public string ModRootPath { get; }
	public IReadOnlyList<string> RelativePaths { get; }
	public IReadOnlyList<string> Diagnostics { get; }
	public bool CanSave { get; }
}

public sealed class DataSchemaModAssetWriteResult
{
	public DataSchemaModAssetWriteResult(bool succeeded, IReadOnlyList<string> writtenRelativePaths, IReadOnlyList<string> diagnostics)
	{
		Succeeded = succeeded;
		WrittenRelativePaths = writtenRelativePaths;
		Diagnostics = diagnostics;
	}

	public bool Succeeded { get; }
	public IReadOnlyList<string> WrittenRelativePaths { get; }
	public IReadOnlyList<string> Diagnostics { get; }
}

/// <summary>
/// Validates schema/record/panel drafts then writes them into a target Mod.
/// Fail-closed: invalid drafts never touch disk; partial write is not success.
/// </summary>
public sealed class DataSchemaModAssetWriter
{
	public const string SchemasRelativePath = "assets/Data/data_schemas.json";
	public const string RecordsRelativePath = "assets/Data/data_records.json";
	public const string PanelsRelativePath = "assets/Panels/panel_templates.json";

	private readonly JsonSerializerOptions _jsonOptions = new()
	{
		WriteIndented = true,
	};

	public DataSchemaModAssetWritePlan Preview(
		string modRootPath,
		JsonArray schemas,
		JsonArray records,
		JsonArray? panelTemplates)
	{
		var diagnostics = new List<string>();
		var paths = new List<string>();

		if (string.IsNullOrWhiteSpace(modRootPath) || !Directory.Exists(modRootPath))
		{
			diagnostics.Add($"Target Mod root does not exist: {modRootPath}");
			return new DataSchemaModAssetWritePlan(modRootPath ?? string.Empty, paths, diagnostics, canSave: false);
		}

		ArgumentNullException.ThrowIfNull(schemas);
		ArgumentNullException.ThrowIfNull(records);

		try
		{
			DataSchemaCatalog catalog = DataSchemaCatalog.Load(schemas);
			_ = DataSchemaRegistry.Load(catalog, records);
			paths.Add(SchemasRelativePath);
			paths.Add(RecordsRelativePath);
		}
		catch (Exception ex)
		{
			diagnostics.Add(ex.Message);
		}

		if (panelTemplates != null)
		{
			try
			{
				for (int i = 0; i < panelTemplates.Count; i++)
				{
					if (panelTemplates[i] is not JsonObject templateObject)
					{
						throw new InvalidOperationException($"Panel template entry[{i}] must be an object.");
					}

					_ = PanelTemplateLoader.Load(templateObject);
				}

				paths.Add(PanelsRelativePath);
			}
			catch (Exception ex)
			{
				diagnostics.Add(ex.Message);
			}
		}

		return new DataSchemaModAssetWritePlan(modRootPath, paths, diagnostics, canSave: diagnostics.Count == 0);
	}

	public DataSchemaModAssetWriteResult Save(
		string modRootPath,
		JsonArray schemas,
		JsonArray records,
		JsonArray? panelTemplates)
	{
		DataSchemaModAssetWritePlan plan = Preview(modRootPath, schemas, records, panelTemplates);
		if (!plan.CanSave)
		{
			return new DataSchemaModAssetWriteResult(false, Array.Empty<string>(), plan.Diagnostics);
		}

		var written = new List<string>();
		var diagnostics = new List<string>();
		var staged = new List<(string finalPath, string tempPath, string relative)>();

		try
		{
			staged.Add(Stage(modRootPath, SchemasRelativePath, schemas.ToJsonString(_jsonOptions)));
			staged.Add(Stage(modRootPath, RecordsRelativePath, records.ToJsonString(_jsonOptions)));
			if (panelTemplates != null)
			{
				staged.Add(Stage(modRootPath, PanelsRelativePath, panelTemplates.ToJsonString(_jsonOptions)));
			}

			foreach ((string finalPath, string tempPath, string relative) in staged)
			{
				Directory.CreateDirectory(Path.GetDirectoryName(finalPath)!);
				if (File.Exists(finalPath))
				{
					File.Delete(finalPath);
				}

				File.Move(tempPath, finalPath);
				written.Add(relative);
			}

			return new DataSchemaModAssetWriteResult(true, written, diagnostics);
		}
		catch (Exception ex)
		{
			foreach ((string _, string tempPath, string _) in staged)
			{
				try
				{
					if (File.Exists(tempPath))
					{
						File.Delete(tempPath);
					}
				}
				catch
				{
					// Best-effort cleanup only; the failure below is the contract.
				}
			}

			diagnostics.Add(ex.Message);
			return new DataSchemaModAssetWriteResult(false, written, diagnostics);
		}
	}

	private static (string finalPath, string tempPath, string relative) Stage(string modRootPath, string relativePath, string contents)
	{
		string finalPath = Path.Combine(modRootPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
		string tempPath = finalPath + ".tmp";
		Directory.CreateDirectory(Path.GetDirectoryName(finalPath)!);
		File.WriteAllText(tempPath, contents, Encoding.UTF8);
		return (finalPath, tempPath, relativePath);
	}
}
