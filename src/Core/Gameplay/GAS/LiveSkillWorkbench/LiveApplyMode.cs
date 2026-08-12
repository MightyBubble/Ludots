using System;
using System.Collections.Generic;
using Ludots.Core.GraphRuntime;

namespace Ludots.Core.Gameplay.GAS.LiveSkillWorkbench
{
    public enum LiveApplyMode : byte
    {
        ImmediateCommand = 1,
        NextCastLiveApply = 2,
        MapReloadRequired = 3,
        EngineRestartRequired = 4
    }

    public readonly record struct LiveApplyClassificationItem(
        LiveDebugPatchOperationKind OperationKind,
        string TargetId,
        LiveApplyMode Mode,
        string Reason,
        IReadOnlyList<LiveEditDiagnostic> Diagnostics);

    public sealed class LiveApplyClassificationReport
    {
        private static readonly LiveApplyClassificationItem[] EmptyItems = Array.Empty<LiveApplyClassificationItem>();

        public LiveApplyClassificationReport(
            Guid sessionId,
            ulong sessionRevision,
            IReadOnlyList<LiveApplyClassificationItem> items,
            bool canCommitImmediate,
            bool canCommitNextCast,
            bool requiresMapReload,
            bool requiresEngineRestart)
        {
            SessionId = sessionId;
            SessionRevision = sessionRevision;
            Items = items ?? EmptyItems;
            CanCommitImmediate = canCommitImmediate;
            CanCommitNextCast = canCommitNextCast;
            RequiresMapReload = requiresMapReload;
            RequiresEngineRestart = requiresEngineRestart;
        }

        public Guid SessionId { get; }
        public ulong SessionRevision { get; }
        public IReadOnlyList<LiveApplyClassificationItem> Items { get; }
        public bool CanCommitImmediate { get; }
        public bool CanCommitNextCast { get; }
        public bool RequiresMapReload { get; }
        public bool RequiresEngineRestart { get; }
    }

    public readonly record struct LiveApplyCommitResult(
        bool Succeeded,
        int AppliedCount,
        IReadOnlyList<LiveEditDiagnostic> Diagnostics);

    /// <summary>
    /// Immediate attribute write sink. Hosts resolve Entity from selection and call AttributeMutationOps.
    /// </summary>
    public interface ILiveAttributeCommandSink
    {
        void Apply(in LiveDebugPatchOperation operation);
    }

    internal sealed class StagedGraphCandidate
    {
        public required string GraphKey { get; init; }
        public required int GraphId { get; init; }
        public required GraphKind Kind { get; init; }
        public required GraphInstruction[] Program { get; init; }
        public required GraphInstructionSourceMap SourceMap { get; init; }
    }

    internal sealed class StagedEffectNumericCandidate
    {
        public required string DefinitionId { get; init; }
        public required int TemplateId { get; init; }
        public required string FieldPath { get; init; }
        public required double NumericValue { get; init; }
    }
}
