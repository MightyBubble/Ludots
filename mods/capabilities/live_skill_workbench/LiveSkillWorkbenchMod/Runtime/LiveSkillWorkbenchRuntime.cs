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
	private readonly object _sync = new();
	private readonly Dictionary<string, CatalogEntry> _catalog = new(StringComparer.Ordinal);
	private readonly Dictionary<string, List<FieldState>> _fieldsByDefinition = new(StringComparer.Ordinal);
	private readonly Dictionary<string, LiveSkillWorkbenchGraphDto> _graphs = new(StringComparer.Ordinal);
	private readonly List<LiveSkillWorkbenchDiagnosticDto> _sessionDiagnostics = new();
	private LiveEditSession _session;
	private LiveGasEditPipeline? _pipeline;
	private LiveApplyClassificationReport? _lastClassification;
	private string _applyMode = LiveSkillWorkbenchIds.ApplyModeNotClassified;
	private bool _applySupported;
	private string _applyStatusLabel = LiveSkillWorkbenchIds.ApplyStatusNotPrechecked;
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

	/// <summary>
	/// Binds the formal LiveGasEditPipeline (from CoreServiceKeys). Required for Precheck/Apply.
	/// </summary>
	public void BindPipeline(LiveGasEditPipeline pipeline)
	{
		ArgumentNullException.ThrowIfNull(pipeline);
		lock (_sync)
		{
			_pipeline = pipeline;
			BumpStateVersionUnlocked();
		}
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
				ApplyMode: _applyMode,
				ApplySupported: _applySupported,
				ApplyStatusLabel: _applyStatusLabel,
				Catalog: catalog,
				Fields: fields,
				Changes: changes,
				Diagnostics: diagnostics,
				Graph: graph,
				EffectChain: effectChain,
				UnavailableActions: BuildUnavailableActionsUnlocked(),
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
				_lastClassification = null;
				_applyMode = LiveSkillWorkbenchIds.ApplyModeNotClassified;
				_applySupported = false;
				_applyStatusLabel = LiveSkillWorkbenchIds.ApplyStatusNotPrechecked;
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
			_lastClassification = null;
			_applyMode = LiveSkillWorkbenchIds.ApplyModeNotClassified;
			_applySupported = false;
			_applyStatusLabel = LiveSkillWorkbenchIds.ApplyStatusNotPrechecked;
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
			"应用到下一次释放需要绑定 LiveGasEditPipeline（GameStart 注入）。改动仅暂存在编辑会话中。");
	}

	public LiveSkillWorkbenchDiagnosticDto CreatePrecheckNotSupportedDiagnostic()
	{
		return new LiveSkillWorkbenchDiagnosticDto(
			"Warning",
			LiveSkillWorkbenchIds.DiagnosticPrecheckNotSupported,
			"预检需要绑定 LiveGasEditPipeline（GameStart 注入）。");
	}

	public bool TryPrecheck(out LiveApplyClassificationReport? report, out LiveSkillWorkbenchDiagnosticDto? error)
	{
		lock (_sync)
		{
			if (_pipeline == null)
			{
				report = null;
				error = CreatePrecheckNotSupportedDiagnostic();
				_sessionDiagnostics.Add(error);
				BumpStateVersionUnlocked();
				return false;
			}

			report = _pipeline.Classify(_session);
			_lastClassification = report;
			ApplyClassificationToUiUnlocked(report);
			for (int i = 0; i < report.Items.Count; i++)
			{
				LiveApplyClassificationItem item = report.Items[i];
				for (int d = 0; d < item.Diagnostics.Count; d++)
				{
					_sessionDiagnostics.Add(ToDto(item.Diagnostics[d]));
				}
			}

			error = null;
			BumpStateVersionUnlocked();
			return true;
		}
	}

	public bool TryApplyNextCast(out LiveApplyCommitResult commit, out LiveSkillWorkbenchDiagnosticDto? error)
	{
		lock (_sync)
		{
			if (_pipeline == null)
			{
				commit = default;
				error = CreateApplyNotSupportedDiagnostic();
				_sessionDiagnostics.Add(error);
				BumpStateVersionUnlocked();
				return false;
			}

			if (_lastClassification == null)
			{
				commit = default;
				error = new LiveSkillWorkbenchDiagnosticDto(
					"Error",
					LiveSkillWorkbenchIds.DiagnosticPrecheckRequired,
					"必须先预检（Classify）再提交安全帧应用。");
				_sessionDiagnostics.Add(error);
				BumpStateVersionUnlocked();
				return false;
			}

			if (!_lastClassification.CanCommitNextCast)
			{
				commit = default;
				string mode = _lastClassification.RequiresEngineRestart
					? LiveSkillWorkbenchIds.ApplyModeEngineRestart
					: LiveSkillWorkbenchIds.ApplyModeMapReload;
				error = new LiveSkillWorkbenchDiagnosticDto(
					"Error",
					LiveEditDiagnosticCodes.EffectFieldNotHotEditable,
					$"当前会话没有可 NextCast 提交的候选（结论：{mode}）。");
				_sessionDiagnostics.Add(error);
				BumpStateVersionUnlocked();
				return false;
			}

			_pipeline.BeginSafeFrame();
			try
			{
				commit = _pipeline.CommitNextCastSafeFrame();
			}
			finally
			{
				_pipeline.EndSafeFrame();
			}

			if (!commit.Succeeded)
			{
				for (int i = 0; i < commit.Diagnostics.Count; i++)
				{
					_sessionDiagnostics.Add(ToDto(commit.Diagnostics[i]));
				}

				error = commit.Diagnostics.Count > 0
					? ToDto(commit.Diagnostics[0])
					: new LiveSkillWorkbenchDiagnosticDto(
						"Error",
						LiveEditDiagnosticCodes.SafeFrameRequired,
						"NextCast 提交失败。");
				BumpStateVersionUnlocked();
				return false;
			}

			_applyStatusLabel = LiveSkillWorkbenchIds.ApplyStatusApplied;
			_applySupported = false;
			error = null;
			BumpStateVersionUnlocked();
			return true;
		}
	}

	private void ApplyClassificationToUiUnlocked(LiveApplyClassificationReport report)
	{
		if (report.RequiresEngineRestart)
		{
			_applyMode = LiveSkillWorkbenchIds.ApplyModeEngineRestart;
			_applySupported = false;
			_applyStatusLabel = LiveSkillWorkbenchIds.ApplyStatusEngineRestart;
			return;
		}

		if (report.RequiresMapReload && !report.CanCommitNextCast)
		{
			_applyMode = LiveSkillWorkbenchIds.ApplyModeMapReload;
			_applySupported = false;
			_applyStatusLabel = LiveSkillWorkbenchIds.ApplyStatusMapReload;
			return;
		}

		if (report.CanCommitNextCast)
		{
			_applyMode = LiveSkillWorkbenchIds.ApplyModeNextCast;
			_applySupported = true;
			_applyStatusLabel = LiveSkillWorkbenchIds.ApplyStatusReadyNextCast;
			return;
		}

		if (report.CanCommitImmediate)
		{
			_applyMode = LiveSkillWorkbenchIds.ApplyModeImmediate;
			_applySupported = false;
			_applyStatusLabel = "预检结论：立即命令（属性）请走 Immediate 路径";
			return;
		}

		_applyMode = LiveSkillWorkbenchIds.ApplyModeNotClassified;
		_applySupported = false;
		_applyStatusLabel = LiveSkillWorkbenchIds.ApplyStatusNotPrechecked;
	}

	private IReadOnlyList<LiveSkillWorkbenchUnavailableActionDto> BuildUnavailableActionsUnlocked()
	{
		var list = new List<LiveSkillWorkbenchUnavailableActionDto>(6)
		{
			new("undo", "撤销", "会话撤销栈尚未接入。"),
			new("redo", "重做", "会话重做栈尚未接入。"),
			new("aiDraft", "AI 生成", "AI 草稿尚未接入（#623）。"),
			new("saveMod", "保存 Mod", "草稿落盘尚未接入（#624）。"),
		};

		if (_pipeline == null)
		{
			list.Add(new LiveSkillWorkbenchUnavailableActionDto(
				"precheck", "预检", "LiveGasEditPipeline 尚未绑定。"));
			list.Add(new LiveSkillWorkbenchUnavailableActionDto(
				"applyNextCast", "应用到下一次释放", "LiveGasEditPipeline 尚未绑定。"));
		}

		return AsReadOnlySnapshot(list);
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

			string applyMode = LiveSkillWorkbenchIds.ApplyModeNotClassified;
			if (_lastClassification != null)
			{
				for (int i = 0; i < _lastClassification.Items.Count; i++)
				{
					LiveApplyClassificationItem item = _lastClassification.Items[i];
					if (string.Equals(item.TargetId, operation.DefinitionId, StringComparison.Ordinal) &&
						item.OperationKind == operation.Kind)
					{
						applyMode = item.Mode.ToString();
						break;
					}
				}
			}

			changes.Add(new LiveSkillWorkbenchChangeDto(
				operation.DefinitionId ?? string.Empty,
				operation.FieldPath ?? string.Empty,
				before,
				operation.NumericValue,
				applyMode));
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
