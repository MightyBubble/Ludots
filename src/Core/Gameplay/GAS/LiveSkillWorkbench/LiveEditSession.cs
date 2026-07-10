using System;
using System.Collections.Generic;

namespace Ludots.Core.Gameplay.GAS.LiveSkillWorkbench
{
    /// <summary>
    /// Runtime edit session for the Real-time Skill Workbench.
    /// Stages structured debug patches without mutating live Ability/Effect/Graph registries.
    /// </summary>
    public sealed class LiveEditSession
    {
        private readonly LiveDebugPatch _patch = new();
        private readonly List<LiveEditDiagnostic> _scratchDiagnostics = new(4);

        private LiveEditSession(Guid sessionId, LiveEditSource source, DateTime createdUtc)
        {
            SessionId = sessionId;
            Source = source;
            CreatedUtc = createdUtc;
            UpdatedUtc = createdUtc;
            Revision = 0;
        }

        public Guid SessionId { get; }

        public LiveEditSource Source { get; }

        public ulong Revision { get; private set; }

        public bool IsDirty => !_patch.IsEmpty;

        public DateTime CreatedUtc { get; }

        public DateTime UpdatedUtc { get; private set; }

        public LiveDebugPatch Patch => _patch;

        public static LiveEditSession Start(LiveEditSource source, DateTime? createdUtc = null)
        {
            if (!IsKnownSource(source))
            {
                throw new ArgumentOutOfRangeException(nameof(source), source, "Unknown live edit source.");
            }

            return new LiveEditSession(Guid.NewGuid(), source, createdUtc ?? DateTime.UtcNow);
        }

        public static LiveEditSession Start(LiveEditSource source, Guid sessionId, DateTime? createdUtc = null)
        {
            if (!IsKnownSource(source))
            {
                throw new ArgumentOutOfRangeException(nameof(source), source, "Unknown live edit source.");
            }

            if (sessionId == Guid.Empty)
            {
                throw new ArgumentException("Session id must be a non-empty Guid.", nameof(sessionId));
            }

            return new LiveEditSession(sessionId, source, createdUtc ?? DateTime.UtcNow);
        }

        /// <summary>
        /// Stages a structured operation into the debug patch.
        /// Invalid edits are rejected with diagnostics and do not mutate the patch.
        /// </summary>
        public LiveEditStageResult TryStage(in LiveDebugPatchOperation operation, DateTime? updatedUtc = null)
        {
            _scratchDiagnostics.Clear();
            Validate(in operation, _scratchDiagnostics);
            if (_scratchDiagnostics.Count > 0)
            {
                return LiveEditStageResult.Failure(Revision, IsDirty, _scratchDiagnostics);
            }

            _patch.Add(in operation);
            Revision++;
            UpdatedUtc = updatedUtc ?? DateTime.UtcNow;
            return LiveEditStageResult.Success(Revision, IsDirty);
        }

        /// <summary>
        /// Discards all staged edits and returns the session to a clean state.
        /// Revision advances so observers can detect the rollback coherently.
        /// </summary>
        public LiveEditStageResult Discard(DateTime? updatedUtc = null)
        {
            _patch.Clear();
            Revision++;
            UpdatedUtc = updatedUtc ?? DateTime.UtcNow;
            return LiveEditStageResult.Success(Revision, isDirty: false);
        }

        private static void Validate(in LiveDebugPatchOperation operation, List<LiveEditDiagnostic> diagnostics)
        {
            if (string.IsNullOrWhiteSpace(operation.Provenance.SourceUri))
            {
                diagnostics.Add(new LiveEditDiagnostic(
                    LiveEditDiagnosticSeverity.Error,
                    LiveEditDiagnosticCodes.MissingProvenanceSourceUri,
                    "Edit provenance sourceUri is required so accepted drafts can later be saved."));
            }

            switch (operation.Kind)
            {
                case LiveDebugPatchOperationKind.SkillEffectNumeric:
                    ValidateSkillEffectNumeric(in operation, diagnostics);
                    break;
                case LiveDebugPatchOperationKind.SelectedActorAttribute:
                    ValidateSelectedActorAttribute(in operation, diagnostics);
                    break;
                default:
                    diagnostics.Add(new LiveEditDiagnostic(
                        LiveEditDiagnosticSeverity.Error,
                        LiveEditDiagnosticCodes.UnsupportedOperationKind,
                        $"Unsupported debug patch operation kind '{operation.Kind}'."));
                    break;
            }
        }

        private static void ValidateSkillEffectNumeric(
            in LiveDebugPatchOperation operation,
            List<LiveEditDiagnostic> diagnostics)
        {
            if (string.IsNullOrWhiteSpace(operation.DefinitionId))
            {
                diagnostics.Add(new LiveEditDiagnostic(
                    LiveEditDiagnosticSeverity.Error,
                    LiveEditDiagnosticCodes.MissingDefinitionId,
                    "Skill/effect numeric edits require a target definition id.",
                    operation.DefinitionId));
            }

            if (string.IsNullOrWhiteSpace(operation.FieldPath))
            {
                diagnostics.Add(new LiveEditDiagnostic(
                    LiveEditDiagnosticSeverity.Error,
                    LiveEditDiagnosticCodes.MissingFieldPath,
                    "Skill/effect numeric edits require a field path/key.",
                    operation.DefinitionId));
            }

            if (!IsFinite(operation.NumericValue))
            {
                diagnostics.Add(new LiveEditDiagnostic(
                    LiveEditDiagnosticSeverity.Error,
                    LiveEditDiagnosticCodes.NonFiniteNumericValue,
                    "Skill/effect numeric edits require a finite numeric value.",
                    operation.DefinitionId));
            }
        }

        private static void ValidateSelectedActorAttribute(
            in LiveDebugPatchOperation operation,
            List<LiveEditDiagnostic> diagnostics)
        {
            if (!operation.ActorTarget.HasTarget)
            {
                diagnostics.Add(new LiveEditDiagnostic(
                    LiveEditDiagnosticSeverity.Error,
                    LiveEditDiagnosticCodes.MissingActorTarget,
                    "Selected actor attribute edits require a selection descriptor or entity id surrogate."));
            }

            if (string.IsNullOrWhiteSpace(operation.AttributeName))
            {
                diagnostics.Add(new LiveEditDiagnostic(
                    LiveEditDiagnosticSeverity.Error,
                    LiveEditDiagnosticCodes.MissingAttributeName,
                    "Selected actor attribute edits require an attribute name/id.",
                    operation.AttributeName));
            }

            if (operation.AttributeMutation is not (ActorAttributeMutationKind.Set or ActorAttributeMutationKind.Add))
            {
                diagnostics.Add(new LiveEditDiagnostic(
                    LiveEditDiagnosticSeverity.Error,
                    LiveEditDiagnosticCodes.UnsupportedAttributeMutation,
                    $"Unsupported attribute mutation '{operation.AttributeMutation}'.",
                    operation.AttributeName));
            }

            if (!IsFinite(operation.NumericValue))
            {
                diagnostics.Add(new LiveEditDiagnostic(
                    LiveEditDiagnosticSeverity.Error,
                    LiveEditDiagnosticCodes.NonFiniteNumericValue,
                    "Selected actor attribute edits require a finite numeric value.",
                    operation.AttributeName));
            }
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private static bool IsKnownSource(LiveEditSource source)
        {
            return source is LiveEditSource.ManualWorkbench
                or LiveEditSource.FileChange
                or LiveEditSource.AiGeneratedDraft;
        }
    }
}
