using System.Collections.Generic;

namespace LiveSkillWorkbenchMod.Contracts;

public sealed record LiveSkillWorkbenchDiagnosticDto(
	string Severity,
	string Code,
	string Message,
	string? TargetId = null);

public sealed record LiveSkillWorkbenchFieldDescriptorDto(
	string FieldPath,
	string Label,
	string ValueKind,
	double? NumericValue,
	double? BaselineValue,
	string? Unit,
	string? Group,
	bool ReadOnly = false,
	double? Min = null,
	double? Max = null,
	double? Step = null,
	string? Description = null,
	string? SourceUri = null);

public sealed record LiveSkillWorkbenchCatalogItemDto(
	string Id,
	string Kind,
	string Label,
	string? ParentId = null,
	string[]? Tags = null);

public sealed record LiveSkillWorkbenchChangeDto(
	string DefinitionId,
	string FieldPath,
	double? BeforeValue,
	double AfterValue,
	string ApplyMode);

public sealed record LiveSkillWorkbenchGraphNodeDto(
	string Id,
	string Label,
	string Kind,
	double X,
	double Y);

public sealed record LiveSkillWorkbenchGraphEdgeDto(
	string Id,
	string Source,
	string Target,
	string? Label = null);

public sealed record LiveSkillWorkbenchGraphDto(
	string DefinitionId,
	IReadOnlyList<LiveSkillWorkbenchGraphNodeDto> Nodes,
	IReadOnlyList<LiveSkillWorkbenchGraphEdgeDto> Edges);

public sealed record LiveSkillWorkbenchEffectChainEventDto(
	string Id,
	string Phase,
	string Label,
	string? DefinitionId,
	string? Detail,
	long Sequence);

public sealed record LiveSkillWorkbenchUnavailableActionDto(
	string ActionId,
	string Label,
	string Reason);

/// <summary>
/// Immutable authored workbench document. Production starts empty unless a real/injected source provides one.
/// </summary>
public sealed record LiveSkillWorkbenchDocumentDto(
	IReadOnlyList<LiveSkillWorkbenchCatalogItemDto> Catalog,
	IReadOnlyList<LiveSkillWorkbenchFieldBindingDto> FieldBindings,
	IReadOnlyList<LiveSkillWorkbenchGraphDto> Graphs,
	IReadOnlyList<LiveSkillWorkbenchEffectChainEventDto> EffectChain,
	string? SelectedCatalogId = null,
	string? SourceUri = null);

/// <summary>
/// Binds a field descriptor to a catalog definition id.
/// </summary>
public sealed record LiveSkillWorkbenchFieldBindingDto(
	string DefinitionId,
	LiveSkillWorkbenchFieldDescriptorDto Field);

/// <summary>
/// Optional document source for host/tests. Null/empty means no authored catalog.
/// </summary>
public interface ILiveSkillWorkbenchDocumentSource
{
	LiveSkillWorkbenchDocumentDto? GetDocument();
}

public sealed record LiveSkillWorkbenchSessionSnapshotDto(
	bool Ready,
	bool Preview,
	string ConnectionState,
	string ModName,
	string SessionId,
	ulong Revision,
	ulong StateVersion,
	bool IsDirty,
	bool HasDocument,
	string? DocumentSourceUri,
	string? SelectedCatalogId,
	string? SelectedCatalogKind,
	string ApplyMode,
	bool ApplySupported,
	string ApplyStatusLabel,
	IReadOnlyList<LiveSkillWorkbenchCatalogItemDto> Catalog,
	IReadOnlyList<LiveSkillWorkbenchFieldDescriptorDto> Fields,
	IReadOnlyList<LiveSkillWorkbenchChangeDto> Changes,
	IReadOnlyList<LiveSkillWorkbenchDiagnosticDto> Diagnostics,
	LiveSkillWorkbenchGraphDto? Graph,
	IReadOnlyList<LiveSkillWorkbenchEffectChainEventDto> EffectChain,
	IReadOnlyList<LiveSkillWorkbenchUnavailableActionDto> UnavailableActions,
	string? Error = null);

public sealed record LiveSkillWorkbenchStageEditRequestDto(
	string DefinitionId,
	string FieldPath,
	double NumericValue,
	string? SourceUri = null,
	string? AuthorNote = null);
