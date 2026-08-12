using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Ludots.Core.Gameplay.GAS.LiveSkillWorkbench
{
    public enum LiveDebugPatchOperationKind : byte
    {
        SkillEffectNumeric = 1,
        SelectedActorAttribute = 2,
        GraphBodyReplace = 3
    }

    public enum ActorAttributeMutationKind : byte
    {
        Set = 1,
        Add = 2
    }

    /// <summary>
    /// Provenance retained so accepted edits can later be saved as Mod config (#624).
    /// This slice records provenance only; it does not persist files.
    /// </summary>
    public readonly struct LiveEditProvenance
    {
        public LiveEditProvenance(
            LiveEditSource source,
            string sourceUri,
            string? authorNote = null,
            string? configRelativePath = null)
        {
            Source = source;
            SourceUri = sourceUri ?? string.Empty;
            AuthorNote = authorNote;
            ConfigRelativePath = configRelativePath;
        }

        public LiveEditSource Source { get; }

        /// <summary>
        /// Stable origin locator (UI control path, watched file URI, or AI draft id).
        /// </summary>
        public string SourceUri { get; }

        public string? AuthorNote { get; }

        /// <summary>
        /// Optional Mod-relative config path hint for later save (#624).
        /// </summary>
        public string? ConfigRelativePath { get; }
    }

    /// <summary>
    /// Selected actor target for an ImmediateCommand-style attribute edit.
    /// Either a selection descriptor, an entity id surrogate, or both may be provided.
    /// </summary>
    public readonly struct ActorTargetSelection
    {
        public ActorTargetSelection(string? selectionDescriptor, int? entityIdSurrogate)
        {
            SelectionDescriptor = selectionDescriptor;
            EntityIdSurrogate = entityIdSurrogate;
        }

        public static ActorTargetSelection FromDescriptor(string selectionDescriptor)
        {
            return new ActorTargetSelection(selectionDescriptor, entityIdSurrogate: null);
        }

        public static ActorTargetSelection FromEntityIdSurrogate(int entityIdSurrogate)
        {
            return new ActorTargetSelection(selectionDescriptor: null, entityIdSurrogate);
        }

        public string? SelectionDescriptor { get; }

        public int? EntityIdSurrogate { get; }

        public bool HasTarget =>
            !string.IsNullOrWhiteSpace(SelectionDescriptor) || EntityIdSurrogate.HasValue;
    }

    /// <summary>
    /// Structured debug patch operation. ManualWorkbench and FileChange share this model.
    /// </summary>
    public readonly struct LiveDebugPatchOperation
    {
        private LiveDebugPatchOperation(
            LiveDebugPatchOperationKind kind,
            LiveEditProvenance provenance,
            string? definitionId,
            string? fieldPath,
            double numericValue,
            ActorTargetSelection actorTarget,
            string? attributeName,
            ActorAttributeMutationKind attributeMutation,
            string? documentJson)
        {
            Kind = kind;
            Provenance = provenance;
            DefinitionId = definitionId;
            FieldPath = fieldPath;
            NumericValue = numericValue;
            ActorTarget = actorTarget;
            AttributeName = attributeName;
            AttributeMutation = attributeMutation;
            DocumentJson = documentJson;
        }

        public LiveDebugPatchOperationKind Kind { get; }

        public LiveEditProvenance Provenance { get; }

        public string? DefinitionId { get; }

        public string? FieldPath { get; }

        public double NumericValue { get; }

        public ActorTargetSelection ActorTarget { get; }

        public string? AttributeName { get; }

        public ActorAttributeMutationKind AttributeMutation { get; }

        /// <summary>
        /// Full graph ControlFlow JSON for <see cref="LiveDebugPatchOperationKind.GraphBodyReplace"/>.
        /// Id/kind in the document must match the live graph identity.
        /// </summary>
        public string? DocumentJson { get; }

        public static LiveDebugPatchOperation SkillEffectNumeric(
            string definitionId,
            string fieldPath,
            double numericValue,
            in LiveEditProvenance provenance)
        {
            return new LiveDebugPatchOperation(
                LiveDebugPatchOperationKind.SkillEffectNumeric,
                provenance,
                definitionId,
                fieldPath,
                numericValue,
                default,
                attributeName: null,
                attributeMutation: default,
                documentJson: null);
        }

        public static LiveDebugPatchOperation SelectedActorAttribute(
            in ActorTargetSelection actorTarget,
            string attributeName,
            ActorAttributeMutationKind mutation,
            double numericValue,
            in LiveEditProvenance provenance)
        {
            return new LiveDebugPatchOperation(
                LiveDebugPatchOperationKind.SelectedActorAttribute,
                provenance,
                definitionId: null,
                fieldPath: null,
                numericValue,
                actorTarget,
                attributeName,
                mutation,
                documentJson: null);
        }

        public static LiveDebugPatchOperation GraphBodyReplace(
            string graphKey,
            string documentJson,
            in LiveEditProvenance provenance)
        {
            return new LiveDebugPatchOperation(
                LiveDebugPatchOperationKind.GraphBodyReplace,
                provenance,
                definitionId: graphKey,
                fieldPath: null,
                numericValue: 0d,
                default,
                attributeName: null,
                attributeMutation: default,
                documentJson);
        }
    }

    /// <summary>
    /// Staged edit collection. Expresses intent only; never writes live registries.
    /// </summary>
    public sealed class LiveDebugPatch
    {
        private readonly List<LiveDebugPatchOperation> _operations = new();
        private readonly ReadOnlyCollection<LiveDebugPatchOperation> _operationsView;

        public LiveDebugPatch()
        {
            _operationsView = _operations.AsReadOnly();
        }

        public int Count => _operations.Count;

        public bool IsEmpty => _operations.Count == 0;

        /// <summary>
        /// Read-only view of staged operations. Not castable to the internal mutable list.
        /// </summary>
        public IReadOnlyList<LiveDebugPatchOperation> Operations => _operationsView;

        internal void Add(in LiveDebugPatchOperation operation)
        {
            _operations.Add(operation);
        }

        internal void Clear()
        {
            _operations.Clear();
        }
    }
}
