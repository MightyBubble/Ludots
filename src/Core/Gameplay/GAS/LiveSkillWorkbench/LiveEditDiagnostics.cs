using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Ludots.Core.Gameplay.GAS.LiveSkillWorkbench
{
    public enum LiveEditDiagnosticSeverity : byte
    {
        Error = 1,
        Warning = 2
    }

    public static class LiveEditDiagnosticCodes
    {
        public const string MissingDefinitionId = "LSW0001";
        public const string MissingFieldPath = "LSW0002";
        public const string NonFiniteNumericValue = "LSW0003";
        public const string MissingAttributeName = "LSW0004";
        public const string MissingActorTarget = "LSW0005";
        public const string UnsupportedAttributeMutation = "LSW0006";
        public const string UnsupportedOperationKind = "LSW0007";
        public const string MissingProvenanceSourceUri = "LSW0008";
    }

    public readonly record struct LiveEditDiagnostic(
        LiveEditDiagnosticSeverity Severity,
        string Code,
        string Message,
        string? TargetId = null);

    public readonly struct LiveEditStageResult
    {
        private static readonly LiveEditDiagnostic[] EmptyDiagnostics = Array.Empty<LiveEditDiagnostic>();

        private LiveEditStageResult(
            bool succeeded,
            ulong revision,
            bool isDirty,
            IReadOnlyList<LiveEditDiagnostic> diagnostics)
        {
            Succeeded = succeeded;
            Revision = revision;
            IsDirty = isDirty;
            Diagnostics = diagnostics ?? EmptyDiagnostics;
        }

        public bool Succeeded { get; }

        public ulong Revision { get; }

        public bool IsDirty { get; }

        /// <summary>
        /// Read-only diagnostics. Failure results snapshot input and cannot be cast back to the caller-owned mutable source.
        /// </summary>
        public IReadOnlyList<LiveEditDiagnostic> Diagnostics { get; }

        public static LiveEditStageResult Success(ulong revision, bool isDirty)
        {
            return new LiveEditStageResult(true, revision, isDirty, EmptyDiagnostics);
        }

        public static LiveEditStageResult Failure(
            ulong revision,
            bool isDirty,
            params LiveEditDiagnostic[] diagnostics)
        {
            if (diagnostics == null || diagnostics.Length == 0)
            {
                throw new ArgumentException("Failure results require at least one diagnostic.", nameof(diagnostics));
            }

            return new LiveEditStageResult(false, revision, isDirty, SnapshotDiagnostics(diagnostics));
        }

        public static LiveEditStageResult Failure(
            ulong revision,
            bool isDirty,
            IReadOnlyList<LiveEditDiagnostic> diagnostics)
        {
            if (diagnostics == null || diagnostics.Count == 0)
            {
                throw new ArgumentException("Failure results require at least one diagnostic.", nameof(diagnostics));
            }

            return new LiveEditStageResult(false, revision, isDirty, SnapshotDiagnostics(diagnostics));
        }

        private static IReadOnlyList<LiveEditDiagnostic> SnapshotDiagnostics(
            IReadOnlyList<LiveEditDiagnostic> diagnostics)
        {
            var copy = new LiveEditDiagnostic[diagnostics.Count];
            for (int i = 0; i < diagnostics.Count; i++)
            {
                copy[i] = diagnostics[i];
            }

            return new ReadOnlyCollection<LiveEditDiagnostic>(copy);
        }
    }
}
