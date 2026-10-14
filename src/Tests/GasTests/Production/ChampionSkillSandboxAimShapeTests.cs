using System;
using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Engine;
using Ludots.Core.Presentation.Presenters;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;
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
                    AssertLineMeters(engine, lengthMeters: 8.4f, widthMeters: 0.7f, rangeMeters: 8.4f);
                    Assert.That(CountAimPresenter(engine, "aim.circle"), Is.EqualTo(0), ShapeDiag(engine, "line excludes circle"));
                    Assert.That(CountAimPresenter(engine, "aim.cone"), Is.EqualTo(0), ShapeDiag(engine, "line excludes cone"));
                    Assert.That(CountAimPresenter(engine, "aim.target"), Is.EqualTo(0), ShapeDiag(engine, "line excludes unit ring"));
                },
                () => LeftClickWorld(engine, backend, new Vector2(400f, 400f), frameTimesMs));

            SelectNamedEntity(engine, backend, "Ezreal Alpha", frameTimesMs);
            Cycle(
                engine,
                backend,
                frameTimesMs,
                "<Keyboard>/r",
                "interaction.context.aim.shape.line",
                () => SetMouseEntity(engine, backend, "Target Dummy A", frameTimesMs),
                () => AssertLineMeters(engine, lengthMeters: 200f, widthMeters: 1.2f, rangeMeters: 22f),
                () => LeftClickWorld(engine, backend, new Vector2(400f, 400f), frameTimesMs));

            SelectNamedEntity(engine, backend, "Jayce Hammer", frameTimesMs);
            Vector2 hammer = ReadPosition(engine.World, "Jayce Hammer");
            Vector2 circlePoint = hammer + new Vector2(200f, 0f);
            Cycle(
                engine,
                backend,
                frameTimesMs,
                "<Keyboard>/q",
                "interaction.context.aim.shape.circle",
                () => SetMouseWorld(engine, backend, circlePoint, frameTimesMs),
                () =>
                {
                    Assert.That(CountAimPresenter(engine, "aim.circle"), Is.EqualTo(1), ShapeDiag(engine, "circle"));
                    Assert.That(CountAimPresenter(engine, "aim.range"), Is.EqualTo(0), ShapeDiag(engine, "circle excludes range"));
                    Assert.That(CountAimPresenter(engine, "aim.line"), Is.EqualTo(0), ShapeDiag(engine, "circle excludes line"));
                    Assert.That(CountAimPresenter(engine, "aim.cone"), Is.EqualTo(0), ShapeDiag(engine, "circle excludes cone"));
                    Assert.That(CountAimPresenter(engine, "aim.target"), Is.EqualTo(0), ShapeDiag(engine, "circle excludes unit ring"));
                    Assert.That(
                        ReadAimPresenterFloat(engine, "aim.circle", WellKnownPresenterParamKeys.OverlayRadius),
                        Is.EqualTo(1.7f).Within(0.02f),
                        ShapeDiag(engine, "circle radius"));
                    AssertAimCircleAt(engine, circlePoint);
                },
                () => LeftClickWorld(engine, backend, circlePoint, frameTimesMs));

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
                    Assert.That(
                        ReadAimPresenterFloat(engine, "aim.cone", WellKnownPresenterParamKeys.OverlayRadius),
                        Is.EqualTo(3f).Within(0.02f),
                        ShapeDiag(engine, "cone radius"));
                    Assert.That(
                        ReadAimPresenterFloat(engine, "aim.cone", WellKnownPresenterParamKeys.OverlayAngle),
                        Is.EqualTo(40f).Within(0.01f),
                        ShapeDiag(engine, "cone angle"));
                    AssertConeMeters(engine, radiusMeters: 3f, halfAngleDegrees: 40f);
                    Assert.That(CountAimPresenter(engine, "aim.range"), Is.EqualTo(0), ShapeDiag(engine, "cone excludes range"));
                    Assert.That(CountAimPresenter(engine, "aim.line"), Is.EqualTo(0), ShapeDiag(engine, "cone excludes line"));
                    Assert.That(CountAimPresenter(engine, "aim.circle"), Is.EqualTo(0), ShapeDiag(engine, "cone excludes circle"));
                    Assert.That(CountAimPresenter(engine, "aim.target"), Is.EqualTo(0), ShapeDiag(engine, "cone excludes unit ring"));
                },
                () => LeftClickWorld(engine, backend, new Vector2(400f, 400f), frameTimesMs));

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
                () => LeftClickWorld(engine, backend, new Vector2(400f, 400f), frameTimesMs));
        }

        [Test]
        public void ChampionSkillSandbox_AimCircle_FollowsThePointer()
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
            Vector2 caster = ReadPosition(engine.World, "Jayce Hammer");
            Vector2 pointA = caster + new Vector2(200f, 0f);
            Vector2 pointB = caster + new Vector2(300f, 100f);
            SetMouseWorld(engine, backend, pointA, frameTimesMs);
            PressAimKey(engine, backend, frameTimesMs, "<Keyboard>/q");
            AssertAimCircleAt(engine, pointA);
            SetMouseWorld(engine, backend, pointB, frameTimesMs);
            AssertAimCircleAt(engine, pointB);
            RightClickWorld(engine, backend, new Vector2(1200f, 900f), frameTimesMs);
            ReleaseButton(engine, backend, "<Keyboard>/q", frameTimesMs);
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

        private static float ReadAimPresenterFloat(GameEngine engine, string definitionKey, int paramKey)
        {
            PresenterDefinitionRegistry definitions = engine.GetService(CoreServiceKeys.PresenterDefinitionRegistry)
                ?? throw new InvalidOperationException("PresenterDefinitionRegistry missing.");
            int defId = definitions.GetId(definitionKey);
            float value = float.NaN;
            int found = 0;
            var query = new QueryDescription().WithAll<PresenterState, PresenterFloatParams>();
            engine.World.Query(in query, (Entity _, ref PresenterState state, ref PresenterFloatParams floats) =>
            {
                if (state.DefId != defId)
                {
                    return;
                }

                found++;
                if (!floats.TryGet(paramKey, out value))
                {
                    value = float.NaN;
                }
            });
            Assert.That(found, Is.EqualTo(1), ShapeDiag(engine, definitionKey));
            return value;
        }

        private static void AssertAimCircleAt(GameEngine engine, Vector2 logicCm)
        {
            GroundOverlayBuffer overlays = engine.GetService(CoreServiceKeys.GroundOverlayBuffer)
                ?? throw new InvalidOperationException("GroundOverlayBuffer missing.");
            bool found = false;
            Vector3 center = default;
            foreach (ref readonly GroundOverlayItem item in overlays.GetSpan())
            {
                if (item.Shape != GroundOverlayShape.Circle)
                {
                    continue;
                }

                if (MathF.Abs(item.FillColor.X - 0.92f) > 0.05f ||
                    MathF.Abs(item.FillColor.Y - 0.62f) > 0.05f ||
                    MathF.Abs(item.FillColor.Z - 0.18f) > 0.05f)
                {
                    continue;
                }

                found = true;
                center = item.Center;
                break;
            }

            Assert.That(found, Is.True, ShapeDiag(engine, "circle overlay"));
            Assert.That(center.X, Is.EqualTo(logicCm.X / 100f).Within(0.05f), ShapeDiag(engine, "circle x"));
            Assert.That(center.Z, Is.EqualTo(logicCm.Y / 100f).Within(0.05f), ShapeDiag(engine, "circle z"));
            GroundOverlayItem circle = RequireOverlay(
                engine,
                GroundOverlayShape.Circle,
                item => AmberFill(item.FillColor));
            Assert.That(circle.Radius, Is.EqualTo(1.7f).Within(0.02f), ShapeDiag(engine, "circle overlay radius"));
        }

        private static void AssertLineMeters(GameEngine engine, float lengthMeters, float widthMeters, float rangeMeters)
        {
            Assert.That(
                ReadAimPresenterFloat(engine, "aim.line", WellKnownPresenterParamKeys.OverlayLength),
                Is.EqualTo(lengthMeters).Within(0.02f),
                ShapeDiag(engine, "line length"));
            Assert.That(
                ReadAimPresenterFloat(engine, "aim.line", WellKnownPresenterParamKeys.OverlayWidth),
                Is.EqualTo(widthMeters).Within(0.02f),
                ShapeDiag(engine, "line width"));
            Assert.That(
                ReadAimPresenterFloat(engine, "aim.range", WellKnownPresenterParamKeys.OverlayRadius),
                Is.EqualTo(rangeMeters).Within(0.02f),
                ShapeDiag(engine, "range radius"));
            GroundOverlayItem line = RequireOverlay(engine, GroundOverlayShape.Line, _ => true);
            Assert.That(line.Length, Is.EqualTo(lengthMeters).Within(0.02f), ShapeDiag(engine, "line overlay length"));
            Assert.That(line.Width, Is.EqualTo(widthMeters).Within(0.02f), ShapeDiag(engine, "line overlay width"));
            GroundOverlayItem ring = RequireOverlay(
                engine,
                GroundOverlayShape.Circle,
                item => RangeFill(item.FillColor));
            Assert.That(ring.Radius, Is.EqualTo(rangeMeters).Within(0.02f), ShapeDiag(engine, "range overlay radius"));
        }

        private static void AssertConeMeters(GameEngine engine, float radiusMeters, float halfAngleDegrees)
        {
            GroundOverlayItem cone = RequireOverlay(engine, GroundOverlayShape.Cone, _ => true);
            Assert.That(cone.Radius, Is.EqualTo(radiusMeters).Within(0.02f), ShapeDiag(engine, "cone overlay radius"));
            Assert.That(cone.Angle, Is.EqualTo(halfAngleDegrees * MathF.PI / 180f).Within(0.02f), ShapeDiag(engine, "cone overlay angle"));
        }

        private static GroundOverlayItem RequireOverlay(
            GameEngine engine,
            GroundOverlayShape shape,
            Func<GroundOverlayItem, bool> match)
        {
            GroundOverlayBuffer overlays = engine.GetService(CoreServiceKeys.GroundOverlayBuffer)
                ?? throw new InvalidOperationException("GroundOverlayBuffer missing.");
            foreach (ref readonly GroundOverlayItem item in overlays.GetSpan())
            {
                if (item.Shape == shape && match(item))
                {
                    return item;
                }
            }

            throw new InvalidOperationException(ShapeDiag(engine, "missing overlay " + shape));
        }

        private static bool AmberFill(Vector4 color)
        {
            return MathF.Abs(color.X - 0.92f) <= 0.05f &&
                   MathF.Abs(color.Y - 0.62f) <= 0.05f &&
                   MathF.Abs(color.Z - 0.18f) <= 0.05f;
        }

        private static bool RangeFill(Vector4 color)
        {
            return MathF.Abs(color.X - 0.28f) <= 0.05f &&
                   MathF.Abs(color.Y - 0.52f) <= 0.05f &&
                   MathF.Abs(color.Z - 0.88f) <= 0.05f;
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
