using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Client;
using Ludots.Core.Engine;
using Ludots.Core.EntityCollections;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace Ludots.Tests.GAS.Production
{
    public sealed partial class ChampionSkillSandboxPlayableAcceptanceTests
    {
        [Test]
        public void ChampionSkillSandbox_AimConfirmClickOnDummy_CastsAndKeepsSelection()
        {
            var frameTimesMs = new List<double>();
            using var engine = CreateEngine();
            var toolbar = engine.GetService(CoreServiceKeys.EntityCommandPanelToolbarProvider)
                ?? throw new System.InvalidOperationException("Toolbar provider missing.");
            var backend = GetInputBackend(engine);
            LoadMap(engine, MapId, frameTimesMs);
            toolbar.Activate(PressReleaseModeId);
            TickUntilFixedTickAdvances(engine, frameTimesMs);

            SelectNamedEntity(engine, backend, "Jayce Cannon", frameTimesMs);
            SetMouseEntity(engine, backend, "Target Dummy A", frameTimesMs);
            PressButton(engine, backend, "<Keyboard>/q", frameTimesMs);
            int errorsBefore = engine.TriggerManager.Errors.Count;
            float healthBefore = ReadHealth(engine.World, "Target Dummy A");

            LeftClickWorld(engine, backend, ReadPosition(engine.World, "Target Dummy A"), frameTimesMs);
            // select_commit can resume on the tick after the release edge.
            TickUntilFixedTickAdvances(engine, frameTimesMs, fixedTicks: 2);

            string diag =
                $"selected={GetSelectedEntityName(engine)} contexts={DescribeActiveInteractionContexts(engine)} " +
                $"triggers={DescribeTriggerErrors(engine)}";
            Assert.Multiple(() =>
            {
                Assert.That(engine.TriggerManager.Errors.Count, Is.EqualTo(errorsBefore), diag);
                Assert.That(GetSelectedEntityName(engine), Is.EqualTo("Jayce Cannon"), diag);
                Assert.That(DescribeActiveInteractionContexts(engine), Does.Not.Contain("interaction.context.core.selecting"), diag);
            });
            TickUntil(
                engine,
                frameTimesMs,
                () => ReadHealth(engine.World, "Target Dummy A") < healthBefore,
                maxFrames: 24,
                describeFailure: () => diag);
            Assert.That(ReadHealth(engine.World, "Target Dummy A"), Is.LessThan(healthBefore), diag);
        }

        [Test]
        public void ChampionSkillSandbox_AimRebindWithoutPreviousActor_DoesNotOpenShape()
        {
            var frameTimesMs = new List<double>();
            using var engine = CreateEngine();
            var toolbar = engine.GetService(CoreServiceKeys.EntityCommandPanelToolbarProvider)
                ?? throw new InvalidOperationException("Toolbar provider missing.");
            var backend = GetInputBackend(engine);
            LoadMap(engine, MapId, frameTimesMs);
            toolbar.Activate(PressReleaseModeId);
            TickUntilFixedTickAdvances(engine, frameTimesMs);

            SelectNamedEntity(engine, backend, "Jayce Hammer", frameTimesMs);
            SetMouseEntity(engine, backend, "Target Dummy C", frameTimesMs);
            HoldButton(engine, backend, "<Keyboard>/e", holdFrames: 2, frameTimesMs);
            Assert.That(
                DescribeActiveInteractionContexts(engine),
                Does.Contain("interaction.context.aim.shape"),
                DescribeTriggerErrors(engine));

            ClearAimActors(engine);
            SelectNamedEntity(engine, backend, "Ezreal Alpha", frameTimesMs);
            TickUntilFixedTickAdvances(engine, frameTimesMs);

            string diag =
                $"contexts={DescribeActiveInteractionContexts(engine)} triggers={DescribeTriggerErrors(engine)}";
            Assert.That(engine.TriggerManager.Errors, Is.Empty, diag);
            Assert.That(diag, Does.Not.Contain("interaction.context.aim.shape"));
            ReleaseButton(engine, backend, "<Keyboard>/e", frameTimesMs);
        }

        private static void ClearAimActors(GameEngine engine)
        {
            Entity owner = ClientLocalSeatAccess.RequireSolePossessedRep(engine);
            EntityCollectionStore collections = engine.GetService(CoreServiceKeys.EntityCollectionStore)
                ?? throw new InvalidOperationException("EntityCollectionStore missing.");
            var descriptor = EntityCollectionDescriptor.Create(
                "aim.actors",
                EntityCollectionSourceKind.Explicit,
                EntityCollectionRoleKind.Display,
                contextEntity: owner,
                primaryEntity: Entity.Null,
                title: "Aim actors",
                summary: "No previous aim actor.");
            collections.Replace(owner, in descriptor, Array.Empty<Entity>(), owner);
        }
    }
}
