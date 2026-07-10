using System;
using System.Collections.Generic;
using Ludots.Core.Gameplay.GAS.LiveSkillWorkbench;
using NUnit.Framework;
using static NUnit.Framework.Assert;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    public sealed class LiveEditSessionTests
    {
        private static readonly DateTime T0 = new(2026, 7, 10, 8, 0, 0, DateTimeKind.Utc);
        private static readonly DateTime T1 = new(2026, 7, 10, 8, 0, 1, DateTimeKind.Utc);
        private static readonly DateTime T2 = new(2026, 7, 10, 8, 0, 2, DateTimeKind.Utc);

        [Test]
        public void Start_CreatesStableSessionWithCleanPatch()
        {
            Guid sessionId = Guid.Parse("11111111-2222-3333-4444-555555555555");
            LiveEditSession session = LiveEditSession.Start(
                LiveEditSource.ManualWorkbench,
                sessionId,
                createdUtc: T0);

            That(session.SessionId, Is.EqualTo(sessionId));
            That(session.Source, Is.EqualTo(LiveEditSource.ManualWorkbench));
            That(session.Revision, Is.EqualTo(0u));
            That(session.IsDirty, Is.False);
            That(session.CreatedUtc, Is.EqualTo(T0));
            That(session.UpdatedUtc, Is.EqualTo(T0));
            That(session.Patch.IsEmpty, Is.True);
            That(session.Patch.Count, Is.EqualTo(0));
        }

        [Test]
        public void Start_AcceptsAllDocumentedSources()
        {
            That(LiveEditSession.Start(LiveEditSource.ManualWorkbench, createdUtc: T0).Source,
                Is.EqualTo(LiveEditSource.ManualWorkbench));
            That(LiveEditSession.Start(LiveEditSource.FileChange, createdUtc: T0).Source,
                Is.EqualTo(LiveEditSource.FileChange));
            That(LiveEditSession.Start(LiveEditSource.AiGeneratedDraft, createdUtc: T0).Source,
                Is.EqualTo(LiveEditSource.AiGeneratedDraft));
        }

        [Test]
        public void TryStage_SkillEffectNumeric_StagesStructuredEditAndMarksDirty()
        {
            LiveEditSession session = LiveEditSession.Start(LiveEditSource.ManualWorkbench, createdUtc: T0);
            var provenance = new LiveEditProvenance(
                LiveEditSource.ManualWorkbench,
                sourceUri: "workbench://ability/Fireball/damage",
                authorNote: "raise fireball damage",
                configRelativePath: "gas/abilities.json");

            LiveEditStageResult result = session.TryStage(
                LiveDebugPatchOperation.SkillEffectNumeric(
                    definitionId: "Fireball",
                    fieldPath: "damage",
                    numericValue: 80d,
                    provenance),
                updatedUtc: T1);

            That(result.Succeeded, Is.True);
            That(result.Revision, Is.EqualTo(1u));
            That(result.IsDirty, Is.True);
            That(result.Diagnostics, Is.Empty);
            That(session.Revision, Is.EqualTo(1u));
            That(session.IsDirty, Is.True);
            That(session.UpdatedUtc, Is.EqualTo(T1));
            That(session.Patch.Count, Is.EqualTo(1));

            LiveDebugPatchOperation staged = session.Patch.Operations[0];
            That(staged.Kind, Is.EqualTo(LiveDebugPatchOperationKind.SkillEffectNumeric));
            That(staged.DefinitionId, Is.EqualTo("Fireball"));
            That(staged.FieldPath, Is.EqualTo("damage"));
            That(staged.NumericValue, Is.EqualTo(80d));
            That(staged.Provenance.Source, Is.EqualTo(LiveEditSource.ManualWorkbench));
            That(staged.Provenance.SourceUri, Is.EqualTo("workbench://ability/Fireball/damage"));
            That(staged.Provenance.ConfigRelativePath, Is.EqualTo("gas/abilities.json"));
        }

        [Test]
        public void TryStage_SelectedActorAttribute_StagesSetAndAddCommands()
        {
            LiveEditSession session = LiveEditSession.Start(LiveEditSource.ManualWorkbench, createdUtc: T0);
            var provenance = new LiveEditProvenance(
                LiveEditSource.ManualWorkbench,
                sourceUri: "workbench://actor/selected/Health");

            LiveEditStageResult setResult = session.TryStage(
                LiveDebugPatchOperation.SelectedActorAttribute(
                    ActorTargetSelection.FromDescriptor("selected"),
                    attributeName: "Health",
                    ActorAttributeMutationKind.Set,
                    numericValue: 100d,
                    provenance),
                updatedUtc: T1);

            LiveEditStageResult addResult = session.TryStage(
                LiveDebugPatchOperation.SelectedActorAttribute(
                    ActorTargetSelection.FromEntityIdSurrogate(42),
                    attributeName: "Mana",
                    ActorAttributeMutationKind.Add,
                    numericValue: 15d,
                    provenance),
                updatedUtc: T2);

            That(setResult.Succeeded, Is.True);
            That(addResult.Succeeded, Is.True);
            That(session.Revision, Is.EqualTo(2u));
            That(session.Patch.Count, Is.EqualTo(2));

            LiveDebugPatchOperation setOp = session.Patch.Operations[0];
            That(setOp.Kind, Is.EqualTo(LiveDebugPatchOperationKind.SelectedActorAttribute));
            That(setOp.ActorTarget.SelectionDescriptor, Is.EqualTo("selected"));
            That(setOp.AttributeName, Is.EqualTo("Health"));
            That(setOp.AttributeMutation, Is.EqualTo(ActorAttributeMutationKind.Set));
            That(setOp.NumericValue, Is.EqualTo(100d));

            LiveDebugPatchOperation addOp = session.Patch.Operations[1];
            That(addOp.ActorTarget.EntityIdSurrogate, Is.EqualTo(42));
            That(addOp.AttributeName, Is.EqualTo("Mana"));
            That(addOp.AttributeMutation, Is.EqualTo(ActorAttributeMutationKind.Add));
            That(addOp.NumericValue, Is.EqualTo(15d));
        }

        [Test]
        public void TryStage_InvalidSkillEffectNumeric_RejectsWithoutMutatingPatch()
        {
            LiveEditSession session = LiveEditSession.Start(LiveEditSource.ManualWorkbench, createdUtc: T0);
            var provenance = new LiveEditProvenance(
                LiveEditSource.ManualWorkbench,
                sourceUri: "workbench://ability/Fireball/damage");

            LiveEditStageResult result = session.TryStage(
                LiveDebugPatchOperation.SkillEffectNumeric(
                    definitionId: " ",
                    fieldPath: "",
                    numericValue: double.NaN,
                    provenance),
                updatedUtc: T1);

            That(result.Succeeded, Is.False);
            That(result.Revision, Is.EqualTo(0u));
            That(result.IsDirty, Is.False);
            That(result.Diagnostics.Count, Is.EqualTo(3));
            That(result.Diagnostics[0].Code, Is.EqualTo(LiveEditDiagnosticCodes.MissingDefinitionId));
            That(result.Diagnostics[1].Code, Is.EqualTo(LiveEditDiagnosticCodes.MissingFieldPath));
            That(result.Diagnostics[2].Code, Is.EqualTo(LiveEditDiagnosticCodes.NonFiniteNumericValue));
            That(session.Revision, Is.EqualTo(0u));
            That(session.IsDirty, Is.False);
            That(session.Patch.IsEmpty, Is.True);
            That(session.UpdatedUtc, Is.EqualTo(T0), "Rejected edits must not bump UpdatedUtc.");
        }

        [Test]
        public void TryStage_InvalidSelectedActorAttribute_RejectsWithoutMutatingPatch()
        {
            LiveEditSession session = LiveEditSession.Start(LiveEditSource.ManualWorkbench, createdUtc: T0);
            var provenance = new LiveEditProvenance(
                LiveEditSource.ManualWorkbench,
                sourceUri: "workbench://actor/selected/Health");

            LiveEditStageResult result = session.TryStage(
                LiveDebugPatchOperation.SelectedActorAttribute(
                    new ActorTargetSelection(selectionDescriptor: " ", entityIdSurrogate: null),
                    attributeName: null!,
                    mutation: (ActorAttributeMutationKind)255,
                    numericValue: double.PositiveInfinity,
                    provenance));

            That(result.Succeeded, Is.False);
            That(session.Patch.IsEmpty, Is.True);
            That(session.IsDirty, Is.False);
            That(result.Diagnostics, Has.Some.Matches<LiveEditDiagnostic>(
                d => d.Code == LiveEditDiagnosticCodes.MissingActorTarget));
            That(result.Diagnostics, Has.Some.Matches<LiveEditDiagnostic>(
                d => d.Code == LiveEditDiagnosticCodes.MissingAttributeName));
            That(result.Diagnostics, Has.Some.Matches<LiveEditDiagnostic>(
                d => d.Code == LiveEditDiagnosticCodes.UnsupportedAttributeMutation));
            That(result.Diagnostics, Has.Some.Matches<LiveEditDiagnostic>(
                d => d.Code == LiveEditDiagnosticCodes.NonFiniteNumericValue));
        }

        [Test]
        public void Discard_ClearsStagedEditsAndAdvancesRevision()
        {
            LiveEditSession session = LiveEditSession.Start(LiveEditSource.ManualWorkbench, createdUtc: T0);
            var provenance = new LiveEditProvenance(
                LiveEditSource.ManualWorkbench,
                sourceUri: "workbench://ability/Fireball/damage");

            That(session.TryStage(
                LiveDebugPatchOperation.SkillEffectNumeric("Fireball", "damage", 80d, provenance),
                updatedUtc: T1).Succeeded,
                Is.True);

            LiveEditStageResult discard = session.Discard(updatedUtc: T2);

            That(discard.Succeeded, Is.True);
            That(discard.Revision, Is.EqualTo(2u));
            That(discard.IsDirty, Is.False);
            That(session.Revision, Is.EqualTo(2u));
            That(session.IsDirty, Is.False);
            That(session.Patch.IsEmpty, Is.True);
            That(session.UpdatedUtc, Is.EqualTo(T2));
        }

        [Test]
        public void ManualAndFileChange_ProduceSamePatchOperationShape()
        {
            LiveEditSession manual = LiveEditSession.Start(LiveEditSource.ManualWorkbench, createdUtc: T0);
            LiveEditSession fileChange = LiveEditSession.Start(LiveEditSource.FileChange, createdUtc: T0);

            var manualProvenance = new LiveEditProvenance(
                LiveEditSource.ManualWorkbench,
                sourceUri: "workbench://ability/Fireball/damage",
                configRelativePath: "gas/abilities.json");
            var fileProvenance = new LiveEditProvenance(
                LiveEditSource.FileChange,
                sourceUri: "file://mods/Demo/gas/abilities.json#Fireball.damage",
                configRelativePath: "gas/abilities.json");

            LiveDebugPatchOperation manualOp = LiveDebugPatchOperation.SkillEffectNumeric(
                "Fireball",
                "damage",
                80d,
                manualProvenance);
            LiveDebugPatchOperation fileOp = LiveDebugPatchOperation.SkillEffectNumeric(
                "Fireball",
                "damage",
                80d,
                fileProvenance);

            That(manual.TryStage(manualOp, updatedUtc: T1).Succeeded, Is.True);
            That(fileChange.TryStage(fileOp, updatedUtc: T1).Succeeded, Is.True);

            LiveDebugPatchOperation stagedManual = manual.Patch.Operations[0];
            LiveDebugPatchOperation stagedFile = fileChange.Patch.Operations[0];

            That(stagedManual.Kind, Is.EqualTo(stagedFile.Kind));
            That(stagedManual.DefinitionId, Is.EqualTo(stagedFile.DefinitionId));
            That(stagedManual.FieldPath, Is.EqualTo(stagedFile.FieldPath));
            That(stagedManual.NumericValue, Is.EqualTo(stagedFile.NumericValue));
            That(stagedManual.Provenance.ConfigRelativePath, Is.EqualTo(stagedFile.Provenance.ConfigRelativePath));
            That(stagedManual.Provenance.Source, Is.Not.EqualTo(stagedFile.Provenance.Source));
            That(stagedManual.Provenance.SourceUri, Is.Not.EqualTo(stagedFile.Provenance.SourceUri));
        }

        [Test]
        public void TryStage_MissingProvenanceSourceUri_IsRejected()
        {
            LiveEditSession session = LiveEditSession.Start(LiveEditSource.AiGeneratedDraft, createdUtc: T0);
            var provenance = new LiveEditProvenance(LiveEditSource.AiGeneratedDraft, sourceUri: " ");

            LiveEditStageResult result = session.TryStage(
                LiveDebugPatchOperation.SkillEffectNumeric("IceNova", "radius", 3.5d, provenance));

            That(result.Succeeded, Is.False);
            That(result.Diagnostics[0].Code, Is.EqualTo(LiveEditDiagnosticCodes.MissingProvenanceSourceUri));
            That(session.Patch.IsEmpty, Is.True);
        }

        [Test]
        public void Operations_CannotBeCastToMutableListAndMutated()
        {
            LiveEditSession session = LiveEditSession.Start(LiveEditSource.ManualWorkbench, createdUtc: T0);
            var provenance = new LiveEditProvenance(
                LiveEditSource.ManualWorkbench,
                sourceUri: "workbench://ability/Fireball/damage");

            That(session.TryStage(
                LiveDebugPatchOperation.SkillEffectNumeric("Fireball", "damage", 80d, provenance),
                updatedUtc: T1).Succeeded,
                Is.True);

            IReadOnlyList<LiveDebugPatchOperation> operations = session.Patch.Operations;

            That(operations, Is.Not.InstanceOf<List<LiveDebugPatchOperation>>());
            That(operations as List<LiveDebugPatchOperation>, Is.Null,
                "Exposed Operations must not be the internal List; cast-and-mutate would bypass TryStage.");

            That(operations.Count, Is.EqualTo(1));
            That(operations[0].DefinitionId, Is.EqualTo("Fireball"));
            That(session.Patch.Count, Is.EqualTo(1));
            That(session.Revision, Is.EqualTo(1u));
        }
    }
}
