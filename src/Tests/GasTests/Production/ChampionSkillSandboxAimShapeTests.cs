using System;
using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Engine;
using Ludots.Core.Presentation.Presenters;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace Ludots.Tests.GAS.Production
{
    public sealed partial class ChampionSkillSandboxPlayableAcceptanceTests
    {
        [Test]
        public void ChampionSkillSandbox_AimShapes_AppearOnPress_ClearOnCancelAndConfirm()
        {
            var frameTimesMs = new List<double>();
            using var engine = CreateEngine();
            var toolbar = engine.GetService(CoreServiceKeys.EntityCommandPanelToolbarProvider)
                ?? throw new InvalidOperationException("Toolbar provider missing.");
            var backend = GetInputBackend(engine);
            LoadMap(engine, MapId, frameTimesMs);

            toolbar.Activate(PressReleaseModeId);
            TickUntilFixedTickAdvances(engine, frameTimesMs);
            Assert.That(GetActiveModeId(engine), Is.EqualTo(PressReleaseModeId));
            Assert.That(CountAimPresenter(engine, "aim.range"), Is.EqualTo(0));
            Assert.That(CountAimPresenter(engine, "aim.line"), Is.EqualTo(0));
            Assert.That(CountAimPresenter(engine, "aim.circle"), Is.EqualTo(0));
            Assert.That(CountAimPresenter(engine, "aim.cone"), Is.EqualTo(0));
            Assert.That(CountAimPresenter(engine, "aim.target"), Is.EqualTo(0));

            SelectNamedEntity(engine, backend, "Ezreal Alpha", frameTimesMs);
            Cycle(
                engine,
                backend,
                frameTimesMs,
                "<Keyboard>/q",
                "interaction.context.aim.shape.line",
                () => SetMouseEntity(engine, backend, "Target Dummy A", frameTimesMs),
                () =>
                {
                    Assert.That(CountAimPresenter(engine, "aim.range"), Is.EqualTo(1), ShapeDiag(engine, "line range"));
                    Assert.That(CountAimPresenter(engine, "aim.line"), Is.EqualTo(1), ShapeDiag(engine, "line"));
                    Assert.That(CountAimPresenter(engine, "aim.circle"), Is.EqualTo(0), ShapeDiag(engine, "line excludes circle"));
                    Assert.That(CountAimPresenter(engine, "aim.cone"), Is.EqualTo(0), ShapeDiag(engine, "line excludes cone"));
                    Assert.That(CountAimPresenter(engine, "aim.target"), Is.EqualTo(0), ShapeDiag(engine, "line excludes unit ring"));
                },
                () => LeftClickWorld(engine, backend, ReadPosition(engine.World, "Target Dummy A"), frameTimesMs));

            SelectNamedEntity(engine, backend, "Ezreal Alpha", frameTimesMs);
            Cycle(
                engine,
                backend,
                frameTimesMs,
                "<Keyboard>/e",
                "interaction.context.aim.shape.circle",
                () => SetMouseWorld(engine, backend, ReadPosition(engine.World, "Ezreal Alpha") + new Vector2(200f, 0f), frameTimesMs),
                () =>
                {
                    Assert.That(CountAimPresenter(engine, "aim.circle"), Is.EqualTo(1), ShapeDiag(engine, "circle"));
                    Assert.That(CountAimPresenter(engine, "aim.range"), Is.EqualTo(0), ShapeDiag(engine, "circle excludes range"));
                    Assert.That(CountAimPresenter(engine, "aim.line"), Is.EqualTo(0), ShapeDiag(engine, "circle excludes line"));
                    Assert.That(CountAimPresenter(engine, "aim.cone"), Is.EqualTo(0), ShapeDiag(engine, "circle excludes cone"));
                    Assert.That(CountAimPresenter(engine, "aim.target"), Is.EqualTo(0), ShapeDiag(engine, "circle excludes unit ring"));
                },
                () => LeftClickWorld(
                    engine,
                    backend,
                    ReadPosition(engine.World, "Ezreal Alpha") + new Vector2(200f, 0f),
                    frameTimesMs));

            SelectNamedEntity(engine, backend, "Jayce Hammer", frameTimesMs);
            Cycle(
                engine,
                backend,
                frameTimesMs,
                "<Keyboard>/e",
                "interaction.context.aim.shape.cone",
                () => SetMouseEntity(engine, backend, "Target Dummy C", frameTimesMs),
                () =>
                {
                    Assert.That(CountAimPresenter(engine, "aim.cone"), Is.EqualTo(1), ShapeDiag(engine, "cone"));
                    Assert.That(CountAimPresenter(engine, "aim.range"), Is.EqualTo(0), ShapeDiag(engine, "cone excludes range"));
                    Assert.That(CountAimPresenter(engine, "aim.line"), Is.EqualTo(0), ShapeDiag(engine, "cone excludes line"));
                    Assert.That(CountAimPresenter(engine, "aim.circle"), Is.EqualTo(0), ShapeDiag(engine, "cone excludes circle"));
                    Assert.That(CountAimPresenter(engine, "aim.target"), Is.EqualTo(0), ShapeDiag(engine, "cone excludes unit ring"));
                },
                () => LeftClickWorld(engine, backend, ReadPosition(engine.World, "Target Dummy C"), frameTimesMs));

            SelectNamedEntity(engine, backend, "Garen Alpha", frameTimesMs);
            Cycle(
                engine,
                backend,
                frameTimesMs,
                "<Keyboard>/r",
                "interaction.context.aim.shape.unit",
                () => SetMouseEntity(engine, backend, "Target Dummy A", frameTimesMs),
                () =>
                {
                    Assert.That(CountAimPresenter(engine, "aim.target"), Is.EqualTo(1), ShapeDiag(engine, "unit ring"));
                    Assert.That(CountAimPresenter(engine, "aim.range"), Is.EqualTo(0), ShapeDiag(engine, "unit excludes range"));
                    Assert.That(CountAimPresenter(engine, "aim.line"), Is.EqualTo(0), ShapeDiag(engine, "unit excludes line"));
                    Assert.That(CountAimPresenter(engine, "aim.circle"), Is.EqualTo(0), ShapeDiag(engine, "unit excludes circle"));
                    Assert.That(CountAimPresenter(engine, "aim.cone"), Is.EqualTo(0), ShapeDiag(engine, "unit excludes cone"));
                },
                () => LeftClickWorld(engine, backend, ReadPosition(engine.World, "Target Dummy A"), frameTimesMs));
        }

        private static void Cycle(
            GameEngine engine,
            TestInputBackend backend,
            List<double> frameTimesMs,
            string key,
            string shapeContext,
            Action aim,
            Action assertShown,
            Action confirm)
        {
            aim();
            PressAimKey(engine, backend, frameTimesMs, key);
            Assert.That(DescribeActiveInteractionContexts(engine), Does.Contain(shapeContext), ShapeDiag(engine, "press context"));
            assertShown();
            RightClickWorld(engine, backend, new Vector2(1200f, 900f), frameTimesMs);
            Assert.That(DescribeActiveInteractionContexts(engine), Does.Not.Contain(shapeContext), ShapeDiag(engine, "cancel context"));
            AssertAimShapesHidden(engine, "cancel");
            ReleaseButton(engine, backend, key, frameTimesMs);

            aim();
            PressAimKey(engine, backend, frameTimesMs, key);
            Assert.That(DescribeActiveInteractionContexts(engine), Does.Contain(shapeContext), ShapeDiag(engine, "confirm press context"));
            assertShown();
            confirm();
            Assert.That(DescribeActiveInteractionContexts(engine), Does.Not.Contain(shapeContext), ShapeDiag(engine, "confirm context"));
            AssertAimShapesHidden(engine, "confirm");
            ReleaseButton(engine, backend, key, frameTimesMs);
        }

        private static void PressAimKey(GameEngine engine, TestInputBackend backend, List<double> frameTimesMs, string key)
        {
            int tickBeforePress = engine.GameSession.CurrentTick;
            backend.SetButton(key, true);
            ApplyPendingGroundOverride(engine);
            TickUntilFixedTickAdvances(engine, frameTimesMs);
            Assert.That(engine.GameSession.CurrentTick, Is.EqualTo(tickBeforePress + 1), ShapeDiag(engine, "press tick"));
        }

        private static void AssertAimShapesHidden(GameEngine engine, string phase)
        {
            Assert.That(CountAimPresenter(engine, "aim.range"), Is.EqualTo(0), ShapeDiag(engine, phase + " range"));
            Assert.That(CountAimPresenter(engine, "aim.line"), Is.EqualTo(0), ShapeDiag(engine, phase + " line"));
            Assert.That(CountAimPresenter(engine, "aim.circle"), Is.EqualTo(0), ShapeDiag(engine, phase + " circle"));
            Assert.That(CountAimPresenter(engine, "aim.cone"), Is.EqualTo(0), ShapeDiag(engine, phase + " cone"));
            Assert.That(CountAimPresenter(engine, "aim.target"), Is.EqualTo(0), ShapeDiag(engine, phase + " unit"));
        }

        private static int CountAimPresenter(GameEngine engine, string definitionKey)
        {
            PresenterDefinitionRegistry definitions = engine.GetService(CoreServiceKeys.PresenterDefinitionRegistry)
                ?? throw new InvalidOperationException("PresenterDefinitionRegistry missing.");
            int defId = definitions.GetId(definitionKey);
            Assert.That(defId, Is.GreaterThan(0), definitionKey);
            int found = 0;
            var query = new QueryDescription().WithAll<PresenterState>();
            engine.World.Query(in query, (Entity _, ref PresenterState state) =>
            {
                if (state.DefId == defId)
                {
                    found++;
                }
            });
            return found;
        }

        private static string ShapeDiag(GameEngine engine, string step)
        {
            return $"{step} || triggers={DescribeTriggerErrors(engine)} || contexts={DescribeActiveInteractionContexts(engine)} " +
                   $"|| range={CountAimPresenter(engine, "aim.range")} line={CountAimPresenter(engine, "aim.line")} " +
                   $"circle={CountAimPresenter(engine, "aim.circle")} cone={CountAimPresenter(engine, "aim.cone")} " +
                   $"target={CountAimPresenter(engine, "aim.target")}";
        }
    }
}
