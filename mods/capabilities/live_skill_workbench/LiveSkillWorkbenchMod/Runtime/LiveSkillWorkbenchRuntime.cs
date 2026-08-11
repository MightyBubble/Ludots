using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Ludots.Core.Gameplay.GAS.LiveSkillWorkbench;
using LiveSkillWorkbenchMod.Contracts;

namespace LiveSkillWorkbenchMod.Runtime;

/// <summary>
/// Workbench session facade over <see cref="LiveEditSession"/>.
/// Owns catalog/descriptor projection for the Web UI; does not write live GAS registries.
/// Starts with no authored document unless <see cref="ReplaceDocument"/> / a document source provides one.
/// </summary>
public sealed class LiveSkillWorkbenchRuntime
{
	private static readonly IReadOnlyList<LiveSkillWorkbenchUnavailableActionDto> UnavailableActionsSnapshot =
		AsReadOnlySnapshot(new[]
		{
			new LiveSkillWorkbenchUnavailableActionDto("undo", "撤销", "会话撤销栈尚未接入。"),
			new LiveSkillWorkbenchUnavailableActionDto("redo", "重做", "会话重做栈尚未接入。"),
			new LiveSkillWorkbenchUnavailableActionDto("precheck", "预检", "候选 GAS 编译尚未接入（#618）。"),
			new LiveSkillWorkbenchUnavailableActionDto("applyNextCast", "应用到下一次释放", "安全帧热应用尚未接入（#618/#619）。"),
			new LiveSkillWorkbenchUnavailableActionDto("aiDraft", "AI 生成", "AI 草稿尚未接入（#623）。"),
			new LiveSkillWorkbenchUnavailableActionDto("saveMod", "保存 Mod", "草稿落盘尚未接入（#624）。"),
		});

	private readonly object _sync = new();
	private readonly Dictionary<string, CatalogEntry> _catalog = new(StringComparer.Ordinal);
	private readonly Dictionary<string, List<FieldState>> _fieldsByDefinition = new(StringComparer.Ordinal);
	private readonly Dictionary<string, LiveSkillWorkbenchGraphDto> _graphs = new(StringComparer.Ordinal);
	private readonly List<LiveSkillWorkbenchDiagnosticDto> _sessionDiagnostics = new();
	private LiveEditSession _session;
	private string? _selectedCatalogId;
	private IReadOnlyList<LiveSkillWorkbenchEffectChainEventDto> _effectChain =
		Array.Empty<LiveSkillWorkbenchEffectChainEventDto>();
	private string? _documentSourceUri;
	private bool _hasDocument;
	private ulong _stateVersion;

	public LiveSkillWorkbenchRuntime(LiveEditSession? session = null)
	{
		_session = session ?? LiveEditSession.Start(LiveEditSource.ManualWorkbench);
	}

	public Guid SessionId
	{
		get
		{
			lock (_sync)
			{
				return _session.SessionId;
			}
		}
	}

	public ulong Revision
	{
		get
		{
			lock (_sync)
			{
				return _session.Revision;
			}
		}
	}

	/// <summary>
	/// Monotonic workbench presentation version. Changes on stage/discard/selection/diagnostics/document replace.
	/// </summary>
	public ulong StateVersion
	{
		get
		{
			lock (_sync)
			{
				return _stateVersion;
			}
		}
	}

	public bool HasDocument
	{
		get
		{
			lock (_sync)
			{
				return _hasDocument;
			}
		}
	}

	/// <summary>
	/// Loads an immutable document from a source. Null/empty source leaves the workbench without authored content.
	/// </summary>
	public void LoadFromSource(ILiveSkillWorkbenchDocumentSource? source)
	{
		LiveSkillWorkbenchDocumentDto? document = source?.GetDocument();
		if (document == null)
		{
			ClearDocument();
			return;
		}

		ReplaceDocument(document);
	}

	public void ClearDocument()
	{
		lock (_sync)
		{
			ClearDocumentUnlocked();
			BumpStateVersionUnlocked();
		}
	}

	public void ReplaceDocument(LiveSkillWorkbenchDocumentDto document)
	{
		ArgumentNullException.ThrowIfNull(document);
		lock (_sync)
		{
			ClearDocumentUnlocked();
			_hasDocument = true;
			_documentSourceUri = document.SourceUri;
			_effectChain = SnapshotEffectChain(document.EffectChain);

			foreach (LiveSkillWorkbenchCatalogItemDto item in document.Catalog ?? Array.Empty<LiveSkillWorkbenchCatalogItemDto>())
			{
				AddCatalog(new CatalogEntry(
					item.Id,
					item.Kind,
					item.Label,
					item.ParentId,
					SnapshotTags(item.Tags)));
			}

			foreach (LiveSkillWorkbenchFieldBindingDto binding in document.FieldBindings ?? Array.Empty<LiveSkillWorkbenchFieldBindingDto>())
			{
				if (!_fieldsByDefinition.TryGetValue(binding.DefinitionId, out List<FieldState>? fields))
				{
					fields = new List<FieldState>();
					_fieldsByDefinition[binding.DefinitionId] = fields;
				}

				fields.Add(FieldState.FromDescriptor(binding.Field));
			}

			foreach (LiveSkillWorkbenchGraphDto graph in document.Graphs ?? Array.Empty<LiveSkillWorkbenchGraphDto>())
			{
				_graphs[graph.DefinitionId] = SnapshotGraph(graph);
			}

			if (!string.IsNullOrWhiteSpace(document.SelectedCatalogId) &&
				_catalog.ContainsKey(document.SelectedCatalogId))
			{
				_selectedCatalogId = document.SelectedCatalogId;
			}
			else
			{
				_selectedCatalogId = null;
			}

			BumpStateVersionUnlocked();
		}
	}

	public LiveSkillWorkbenchSessionSnapshotDto BuildSnapshot(string connectionState, bool preview = false, string? error = null)
	{
		lock (_sync)
		{
			string? selectedId = _selectedCatalogId;
			string? selectedKind = selectedId != null && _catalog.TryGetValue(selectedId, out CatalogEntry? selected)
				? selected.Kind
				: null;

			IReadOnlyList<LiveSkillWorkbenchFieldDescriptorDto> fields = AsReadOnlySnapshot(BuildFields(selectedId));
			IReadOnlyList<LiveSkillWorkbenchChangeDto> changes = AsReadOnlySnapshot(BuildChanges());
			IReadOnlyList<LiveSkillWorkbenchDiagnosticDto> diagnostics = AsReadOnlySnapshot(_sessionDiagnostics);
			IReadOnlyList<LiveSkillWorkbenchCatalogItemDto> catalog = AsReadOnlySnapshot(
				_catalog.Values
					.Select(entry => new LiveSkillWorkbenchCatalogItemDto(
						entry.Id,
						entry.Kind,
						entry.Label,
						entry.ParentId,
						SnapshotTags(entry.Tags)))
					.ToList());
			IReadOnlyList<LiveSkillWorkbenchEffectChainEventDto> effectChain = SnapshotEffectChain(_effectChain);
			LiveSkillWorkbenchGraphDto? graph = SnapshotGraphOrNull(ResolveGraph(selectedId));

			return new LiveSkillWorkbenchSessionSnapshotDto(
				Ready: error == null,
				Preview: preview,
				ConnectionState: connectionState,
				ModName: "LiveSkillWorkbenchMod",
				SessionId: _session.SessionId.ToString("D"),
				Revision: _session.Revision,
				StateVersion: _stateVersion,
				IsDirty: _session.IsDirty,
				HasDocument: _hasDocument,
				DocumentSourceUri: _documentSourceUri,
				SelectedCatalogId: selectedId,
				SelectedCatalogKind: selectedKind,
				ApplyMode: LiveSkillWorkbenchIds.ApplyModeNotClassified,
				ApplySupported: false,
				ApplyStatusLabel: LiveSkillWorkbenchIds.ApplyStatusNotPrechecked,
				Catalog: catalog,
				Fields: fields,
				Changes: changes,
				Diagnostics: diagnostics,
				Graph: graph,
				EffectChain: effectChain,
				UnavailableActions: UnavailableActionsSnapshot,
				Error: error);
		}
	}

	public LiveEditStageResult StageEdit(LiveSkillWorkbenchStageEditRequestDto request)
	{
		ArgumentNullException.ThrowIfNull(request);
		lock (_sync)
		{
			if (string.IsNullOrWhiteSpace(request.DefinitionId) ||
				!_fieldsByDefinition.TryGetValue(request.DefinitionId, out List<FieldState>? fields))
			{
				return RejectStageUnlocked(
					request.DefinitionId,
					LiveEditDiagnosticCodes.MissingDefinitionId,
					$"Unknown definition '{request.DefinitionId}'.");
			}

			FieldState? field = fields.Find(candidate =>
				string.Equals(candidate.FieldPath, request.FieldPath, StringComparison.Ordinal));
			if (field == null)
			{
				return RejectStageUnlocked(
					request.DefinitionId,
					LiveEditDiagnosticCodes.MissingFieldPath,
					$"Unknown field path '{request.FieldPath}' on '{request.DefinitionId}'.");
			}

			if (field.ReadOnly)
			{
				return RejectStageUnlocked(
					request.DefinitionId,
					LiveSkillWorkbenchIds.DiagnosticFieldReadOnly,
					$"Field '{request.FieldPath}' on '{request.DefinitionId}' is read-only and cannot be staged.");
			}

			if (double.IsFinite(request.NumericValue))
			{
				if (field.Min is double min && request.NumericValue < min)
				{
					return RejectStageUnlocked(
						request.DefinitionId,
						LiveSkillWorkbenchIds.DiagnosticValueBelowMin,
						$"Value {request.NumericValue} for '{request.FieldPath}' is below Min {min}.");
				}

				if (field.Max is double max && request.NumericValue > max)
				{
					return RejectStageUnlocked(
						request.DefinitionId,
						LiveSkillWorkbenchIds.DiagnosticValueAboveMax,
						$"Value {request.NumericValue} for '{request.FieldPath}' is above Max {max}.");
				}
			}

			string sourceUri = string.IsNullOrWhiteSpace(request.SourceUri)
				? field.SourceUri ?? $"workbench://{request.DefinitionId}/{request.FieldPath}"
				: request.SourceUri!;
			var provenance = new LiveEditProvenance(
				LiveEditSource.ManualWorkbench,
				sourceUri,
				request.AuthorNote);

			LiveEditStageResult result = _session.TryStage(
				LiveDebugPatchOperation.SkillEffectNumeric(
					request.DefinitionId,
					request.FieldPath,
					request.NumericValue,
					provenance));

			if (result.Succeeded)
			{
				field.CurrentValue = request.NumericValue;
				_sessionDiagnostics.RemoveAll(static d =>
					string.Equals(d.Code, LiveEditDiagnosticCodes.MissingDefinitionId, StringComparison.Ordinal) ||
					string.Equals(d.Code, LiveEditDiagnosticCodes.MissingFieldPath, StringComparison.Ordinal) ||
					string.Equals(d.Code, LiveEditDiagnosticCodes.NonFiniteNumericValue, StringComparison.Ordinal) ||
					string.Equals(d.Code, LiveEditDiagnosticCodes.MissingProvenanceSourceUri, StringComparison.Ordinal) ||
					string.Equals(d.Code, LiveSkillWorkbenchIds.DiagnosticFieldReadOnly, StringComparison.Ordinal) ||
					string.Equals(d.Code, LiveSkillWorkbenchIds.DiagnosticValueBelowMin, StringComparison.Ordinal) ||
					string.Equals(d.Code, LiveSkillWorkbenchIds.DiagnosticValueAboveMax, StringComparison.Ordinal));
			}
			else
			{
				foreach (LiveEditDiagnostic diagnostic in result.Diagnostics)
				{
					_sessionDiagnostics.Add(ToDto(diagnostic));
				}
			}

			BumpStateVersionUnlocked();
			return result;
		}
	}

	public LiveEditStageResult DiscardEdits()
	{
		lock (_sync)
		{
			foreach (List<FieldState> fields in _fieldsByDefinition.Values)
			{
				foreach (FieldState field in fields)
				{
					field.CurrentValue = field.BaselineValue;
				}
			}

			_sessionDiagnostics.Clear();
			LiveEditStageResult result = _session.Discard();
			BumpStateVersionUnlocked();
			return result;
		}
	}

	public bool SelectCatalogItem(string catalogId)
	{
		lock (_sync)
		{
			if (!_catalog.ContainsKey(catalogId))
			{
				return false;
			}

			if (string.Equals(_selectedCatalogId, catalogId, StringComparison.Ordinal))
			{
				return true;
			}

			_selectedCatalogId = catalogId;
			BumpStateVersionUnlocked();
			return true;
		}
	}

	public LiveSkillWorkbenchDiagnosticDto CreateApplyNotSupportedDiagnostic()
	{
		return new LiveSkillWorkbenchDiagnosticDto(
			"Warning",
			LiveSkillWorkbenchIds.DiagnosticApplyNotSupported,
			"应用到下一次释放尚未接入（#618/#619）。改动仅暂存在编辑会话中，不会写入运行中的 GAS。");
	}

	public LiveSkillWorkbenchDiagnosticDto CreatePrecheckNotSupportedDiagnostic()
	{
		return new LiveSkillWorkbenchDiagnosticDto(
			"Warning",
			LiveSkillWorkbenchIds.DiagnosticPrecheckNotSupported,
			"预检（候选 GAS 编译）尚未接入（#618）。");
	}

	public void RecordDiagnostic(LiveSkillWorkbenchDiagnosticDto diagnostic)
	{
		ArgumentNullException.ThrowIfNull(diagnostic);
		lock (_sync)
		{
			_sessionDiagnostics.Add(diagnostic);
			BumpStateVersionUnlocked();
		}
	}

	private LiveEditStageResult RejectStageUnlocked(string? targetId, string code, string message)
	{
		var diagnostic = new LiveEditDiagnostic(
			LiveEditDiagnosticSeverity.Error,
			code,
			message,
			targetId);
		_sessionDiagnostics.Add(ToDto(diagnostic));
		BumpStateVersionUnlocked();
		return LiveEditStageResult.Failure(_session.Revision, _session.IsDirty, diagnostic);
	}

	private void ClearDocumentUnlocked()
	{
		_catalog.Clear();
		_fieldsByDefinition.Clear();
		_graphs.Clear();
		_sessionDiagnostics.Clear();
		_effectChain = Array.Empty<LiveSkillWorkbenchEffectChainEventDto>();
		_selectedCatalogId = null;
		_documentSourceUri = null;
		_hasDocument = false;
		_session = LiveEditSession.Start(LiveEditSource.ManualWorkbench);
	}

	private void BumpStateVersionUnlocked()
	{
		_stateVersion++;
	}

	private LiveSkillWorkbenchGraphDto? ResolveGraph(string? selectedId)
	{
		if (selectedId == null)
		{
			return null;
		}

		if (_graphs.TryGetValue(selectedId, out LiveSkillWorkbenchGraphDto? direct))
		{
			return direct;
		}

		foreach (CatalogEntry entry in _catalog.Values)
		{
			if (!string.Equals(entry.Kind, "graph", StringComparison.Ordinal) ||
				!string.Equals(entry.ParentId, selectedId, StringComparison.Ordinal))
			{
				continue;
			}

			if (_graphs.TryGetValue(entry.Id, out LiveSkillWorkbenchGraphDto? child))
			{
				return child;
			}
		}

		return null;
	}

	private List<LiveSkillWorkbenchFieldDescriptorDto> BuildFields(string? selectedId)
	{
		if (selectedId == null || !_fieldsByDefinition.TryGetValue(selectedId, out List<FieldState>? fields))
		{
			return new List<LiveSkillWorkbenchFieldDescriptorDto>();
		}

		var result = new List<LiveSkillWorkbenchFieldDescriptorDto>(fields.Count);
		foreach (FieldState field in fields)
		{
			result.Add(field.ToDescriptor());
		}

		return result;
	}

	private List<LiveSkillWorkbenchChangeDto> BuildChanges()
	{
		var changes = new List<LiveSkillWorkbenchChangeDto>();
		foreach (LiveDebugPatchOperation operation in _session.Patch.Operations)
		{
			if (operation.Kind != LiveDebugPatchOperationKind.SkillEffectNumeric)
			{
				continue;
			}

			double? before = null;
			if (operation.DefinitionId != null &&
				operation.FieldPath != null &&
				_fieldsByDefinition.TryGetValue(operation.DefinitionId, out List<FieldState>? fields))
			{
				FieldState? field = fields.Find(candidate =>
					string.Equals(candidate.FieldPath, operation.FieldPath, StringComparison.Ordinal));
				before = field?.BaselineValue;
			}

			changes.Add(new LiveSkillWorkbenchChangeDto(
				operation.DefinitionId ?? string.Empty,
				operation.FieldPath ?? string.Empty,
				before,
				operation.NumericValue,
				LiveSkillWorkbenchIds.ApplyModeNotClassified));
		}

		return changes;
	}

	private void AddCatalog(CatalogEntry entry)
	{
		_catalog[entry.Id] = entry;
	}

	private static LiveSkillWorkbenchDiagnosticDto ToDto(LiveEditDiagnostic diagnostic)
	{
		return new LiveSkillWorkbenchDiagnosticDto(
			diagnostic.Severity.ToString(),
			diagnostic.Code,
			diagnostic.Message,
			diagnostic.TargetId);
	}

	private static string[] SnapshotTags(string[]? tags)
	{
		if (tags == null || tags.Length == 0)
		{
			return Array.Empty<string>();
		}

		var copy = new string[tags.Length];
		Array.Copy(tags, copy, tags.Length);
		return copy;
	}

	private static LiveSkillWorkbenchGraphDto SnapshotGraph(LiveSkillWorkbenchGraphDto graph)
	{
		ArgumentNullException.ThrowIfNull(graph);
		return new LiveSkillWorkbenchGraphDto(
			graph.DefinitionId,
			AsReadOnlySnapshot(graph.Nodes),
			AsReadOnlySnapshot(graph.Edges));
	}

	private static LiveSkillWorkbenchGraphDto? SnapshotGraphOrNull(LiveSkillWorkbenchGraphDto? graph)
	{
		return graph == null ? null : SnapshotGraph(graph);
	}

	private static IReadOnlyList<LiveSkillWorkbenchEffectChainEventDto> SnapshotEffectChain(
		IReadOnlyList<LiveSkillWorkbenchEffectChainEventDto>? events)
	{
		return AsReadOnlySnapshot(events);
	}

	private static IReadOnlyList<T> AsReadOnlySnapshot<T>(IReadOnlyList<T>? source)
	{
		if (source == null || source.Count == 0)
		{
			return Array.Empty<T>();
		}

		var copy = new T[source.Count];
		for (int i = 0; i < source.Count; i++)
		{
			copy[i] = source[i];
		}

		return new ReadOnlyCollection<T>(copy);
	}

	private sealed class CatalogEntry
	{
		public CatalogEntry(string id, string kind, string label, string? parentId, string[] tags)
		{
			Id = id;
			Kind = kind;
			Label = label;
			ParentId = parentId;
			Tags = tags;
		}

		public string Id { get; }
		public string Kind { get; }
		public string Label { get; }
		public string? ParentId { get; }
		public string[] Tags { get; }
	}

	private sealed class FieldState
	{
		public FieldState(
			string fieldPath,
			string label,
			double baselineValue,
			double currentValue,
			string? unit,
			string? group,
			bool readOnly,
			double? min,
			double? max,
			double? step,
			string? description,
			string? sourceUri)
		{
			FieldPath = fieldPath;
			Label = label;
			BaselineValue = baselineValue;
			CurrentValue = currentValue;
			Unit = unit;
			Group = group;
			ReadOnly = readOnly;
			Min = min;
			Max = max;
			Step = step;
			Description = description;
			SourceUri = sourceUri;
		}

		public string FieldPath { get; }
		public string Label { get; }
		public double BaselineValue { get; }
		public double CurrentValue { get; set; }
		public string? Unit { get; }
		public string? Group { get; }
		public bool ReadOnly { get; }
		public double? Min { get; }
		public double? Max { get; }
		public double? Step { get; }
		public string? Description { get; }
		public string? SourceUri { get; }

		public static FieldState FromDescriptor(LiveSkillWorkbenchFieldDescriptorDto field)
		{
			ArgumentNullException.ThrowIfNull(field);
			double baseline = field.BaselineValue ?? field.NumericValue ?? 0d;
			double current = field.NumericValue ?? baseline;
			return new FieldState(
				field.FieldPath,
				field.Label,
				baseline,
				current,
				field.Unit,
				field.Group,
				field.ReadOnly,
				field.Min,
				field.Max,
				field.Step,
				field.Description,
				field.SourceUri);
		}

		public LiveSkillWorkbenchFieldDescriptorDto ToDescriptor()
		{
			return new LiveSkillWorkbenchFieldDescriptorDto(
				FieldPath,
				Label,
				"number",
				CurrentValue,
				BaselineValue,
				Unit,
				Group,
				ReadOnly,
				Min,
				Max,
				Step,
				Description,
				SourceUri);
		}
	}
}
