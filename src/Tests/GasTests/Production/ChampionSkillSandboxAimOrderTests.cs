using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace Ludots.Tests.GAS.Production
{
    public sealed partial class ChampionSkillSandboxPlayableAcceptanceTests
    {
        [Test]
        public void ChampionSkillSandbox_KbmCastModes_SubmitSlotTargetOrPoint_AndLaserFollowsPointer()
        {
            var frameTimesMs = new List<double>();
            using var engine = CreateEngine();
            var toolbar = engine.GetService(CoreServiceKeys.EntityCommandPanelToolbarProvider)
                ?? throw new InvalidOperationException("Toolbar provider missing.");
            var backend = GetInputBackend(engine);
            LoadMap(engine, MapId, frameTimesMs);
            Assert.That(GetActiveModeId(engine), Is.EqualTo(SmartCastModeId));

            SelectNamedEntity(engine, backend, "Ezreal Alpha", frameTimesMs);
            SetMouseEntity(engine, backend, "Target Dummy A", frameTimesMs);
            Entity ezreal = FindEntityByName(engine.World, "Ezreal Alpha");
            Entity dummyA = FindEntityByName(engine.World, "Target Dummy A");
            float healthBeforeSmart = ReadHealth(engine.World, "Target Dummy A");
            backend.SetButton("<Keyboard>/q", true);
            ApplyPendingGroundOverride(engine);
            TickUntilFixedTickAdvances(engine, frameTimesMs);
            backend.SetButton("<Keyboard>/q", false);
            TickUntilFixedTickAdvances(engine, frameTimesMs);
            TickUntil(
                engine,
                frameTimesMs,
                () => ReadHealth(engine.World, "Target Dummy A") < healthBeforeSmart,
                maxFrames: 48,
                describeFailure: () =>
                {
                    string facing;
                    try
                    {
                        facing = ReadAimFloat(engine, ezreal, OrderBlackboardKeys.Cast_Facing).ToString("0.##");
                    }
                    catch (InvalidOperationException)
                    {
                        facing = "<missing>";
                    }

                    bool cooled = EntityHasTag(engine.World, "Ezreal Alpha", "Cooldown.Champion.Ezreal.Q");
                    return $"triggers={DescribeTriggerErrors(engine)} || {BuildAbilityDiagnostics(engine, "Ezreal Alpha")} || aim={DescribeAim(engine, ezreal)} point=({TryReadAimFloat(engine, ezreal, "aim.point.x_cm")},{TryReadAimFloat(engine, ezreal, "aim.point.y_cm")}) facing={facing} cooled={cooled}";
                });
            Assert.That(ReadAimInt(engine, ezreal, "aim.slot"), Is.EqualTo(0));
            Assert.That(ReadAimEntity(engine, ezreal, "aim.target"), Is.EqualTo(dummyA));
            Assert.That(ReadHealth(engine.World, "Target Dummy A"), Is.LessThan(healthBeforeSmart));

            toolbar.Activate(IndicatorModeId);
            Tick(engine, 1, frameTimesMs);
            Assert.That(GetActiveModeId(engine), Is.EqualTo(IndicatorModeId));
            SetMouseEntity(engine, backend, "Target Dummy A", frameTimesMs);
            float healthBeforeRelease = ReadHealth(engine.World, "Target Dummy A");
            HoldButton(engine, backend, "<Keyboard>/r", holdFrames: 2, frameTimesMs);
            TickUntil(
                engine,
                frameTimesMs,
                () => DescribeActiveInteractionContexts(engine).Contains("interaction.context.aim.release", StringComparison.Ordinal),
                maxFrames: 24);
            Assert.That(ReadHealth(engine.World, "Target Dummy A"), Is.EqualTo(healthBeforeRelease).Within(0.001f));
            Assert.That(engine.World.Has<AbilityExecInstance>(ezreal), Is.False);
            ReleaseButton(engine, backend, "<Keyboard>/r", frameTimesMs);
            TickUntilHealthChanges(engine, frameTimesMs, "Target Dummy A", healthBeforeRelease, maxFrames: 48);
            Assert.That(ReadAimInt(engine, ezreal, "aim.slot"), Is.EqualTo(3));
            Assert.That(ReadAimEntity(engine, ezreal, "aim.target"), Is.EqualTo(dummyA));

            SelectNamedEntity(engine, backend, "Jayce Cannon", frameTimesMs);
            toolbar.Activate(PressReleaseModeId);
            Tick(engine, 1, frameTimesMs);
            Assert.That(GetActiveModeId(engine), Is.EqualTo(PressReleaseModeId));
            Entity jayce = FindEntityByName(engine.World, "Jayce Cannon");
            SetMouseEntity(engine, backend, "Target Dummy A", frameTimesMs);
            float healthBeforeCancel = ReadHealth(engine.World, "Target Dummy A");
            PressButton(engine, backend, "<Keyboard>/q", frameTimesMs);
            TickUntil(
                engine,
                frameTimesMs,
                () => DescribeActiveInteractionContexts(engine).Contains("interaction.context.aim.click", StringComparison.Ordinal),
                maxFrames: 24);
            Assert.That(ReadAimInt(engine, jayce, "aim.slot"), Is.EqualTo(0));
            Assert.That(ReadAimInt(engine, jayce, "aim.point.valid"), Is.EqualTo(1));
            RightClickWorld(engine, backend, ReadPosition(engine.World, "Target Dummy A"), frameTimesMs);
            TickUntil(
                engine,
                frameTimesMs,
                () => !DescribeActiveInteractionContexts(engine).Contains("interaction.context.aim.click", StringComparison.Ordinal),
                maxFrames: 24);
            Assert.That(ReadHealth(engine.World, "Target Dummy A"), Is.EqualTo(healthBeforeCancel).Within(0.001f));
            Assert.That(engine.World.Has<AbilityExecInstance>(jayce), Is.False);
            Assert.That(DescribeActiveInteractionContexts(engine), Does.Not.Contain("interaction.context.aim.click"));

            PressButton(engine, backend, "<Keyboard>/q", frameTimesMs);
            TickUntil(
                engine,
                frameTimesMs,
                () => DescribeActiveInteractionContexts(engine).Contains("interaction.context.aim.click", StringComparison.Ordinal),
                maxFrames: 24);
            LeftClickWorld(engine, backend, ReadPosition(engine.World, "Target Dummy A"), frameTimesMs);
            TickUntilHealthChanges(engine, frameTimesMs, "Target Dummy A", healthBeforeCancel, maxFrames: 48);
            Assert.That(ReadAimInt(engine, jayce, "aim.slot"), Is.EqualTo(0));
            Assert.That(ReadAimEntity(engine, jayce, "aim.target"), Is.EqualTo(dummyA));
            Assert.That(ReadHealth(engine.World, "Target Dummy A"), Is.LessThan(healthBeforeCancel));

            SelectNamedEntity(engine, backend, "Geomancer Alpha", frameTimesMs);
            toolbar.Activate(SmartCastModeId);
            Tick(engine, 1, frameTimesMs);
            Entity geomancer = FindEntityByName(engine.World, "Geomancer Alpha");
            Vector2 beaconPoint = ReadPosition(engine.World, "Geomancer Alpha") + new Vector2(-200f, 0f);
            SetMouseWorld(engine, backend, beaconPoint, frameTimesMs);
            PressButton(engine, backend, "<Keyboard>/q", frameTimesMs);
            TickUntil(
                engine,
                frameTimesMs,
                () => EntityExists(engine.World, "Runic Beacon"),
                maxFrames: 36);
            Assert.That(ReadAimInt(engine, geomancer, "aim.slot"), Is.EqualTo(0));
            Assert.That(ReadAimInt(engine, geomancer, "aim.point.valid"), Is.EqualTo(1));
            Assert.That(engine.World.IsAlive(ReadAimEntity(engine, geomancer, "aim.target")), Is.False);
            Vector2 beaconPosition = ReadPosition(engine.World, "Runic Beacon");
            Assert.That(Vector2.Distance(beaconPosition, beaconPoint), Is.LessThanOrEqualTo(80f));

            SelectNamedEntity(engine, backend, "Spell Engineer Alpha", frameTimesMs);
            Entity engineer = FindEntityByName(engine.World, "Spell Engineer Alpha");
            SetMouseEntity(engine, backend, "Target Dummy D", frameTimesMs);
            HoldButton(engine, backend, "<Keyboard>/r", holdFrames: 2, frameTimesMs);
            TickUntil(
                engine,
                frameTimesMs,
                () => EntityExists(engine.World, "Guided Laser") && engine.World.Has<AbilityExecInstance>(engineer),
                maxFrames: 36);
            AbilityExecInstance laserExec = engine.World.Get<AbilityExecInstance>(engineer);
            Assert.That(laserExec.AbilitySlot, Is.EqualTo(3));
            float facingAtD = ReadAimFloat(engine, engineer, OrderBlackboardKeys.Cast_Facing);
            float laserFacingAtD = ReadFacingRadians(engine.World, "Guided Laser");
            SetMouseEntity(engine, backend, "Target Dummy E", frameTimesMs);
            TickUntil(
                engine,
                frameTimesMs,
                () => MathF.Abs(NormalizeRadians(ReadFacingRadians(engine.World, "Guided Laser") - laserFacingAtD)) > 0.2f,
                maxFrames: 36);
            float facingAtE = ReadAimFloat(engine, engineer, OrderBlackboardKeys.Cast_Facing);
            Assert.That(MathF.Abs(facingAtE - facingAtD), Is.GreaterThan(1f));
            Assert.That(engine.World.Get<AbilityExecInstance>(engineer).AbilitySlot, Is.EqualTo(3));
        }

        private static string DescribeTriggerErrors(GameEngine engine)
        {
            if (engine.TriggerManager.Errors.Count == 0)
            {
                return "<none>";
            }

            return string.Join(" | ", engine.TriggerManager.Errors.Take(4).Select(error => $"{error.TriggerName}: {error.Exception.Message}"));
        }

        private static string DescribeAim(GameEngine engine, Entity actor)
        {
            if (!engine.World.IsAlive(actor))
            {
                return "<dead>";
            }

            string slot = TryReadAimInt(engine, actor, "aim.slot");
            string valid = TryReadAimInt(engine, actor, "aim.point.valid");
            string target = "<missing>";
            try
            {
                Entity aimed = ReadAimEntity(engine, actor, "aim.target");
                target = engine.World.IsAlive(aimed) && engine.World.TryGet(aimed, out Name name)
                    ? name.Value
                    : aimed.ToString();
            }
            catch (InvalidOperationException ex)
            {
                target = ex.Message;
            }

            return $"slot={slot} valid={valid} target={target}";
        }

        private static string TryReadAimFloat(GameEngine engine, Entity actor, string key)
        {
            try
            {
                int keyId = ConfigKeyRegistry.GetId(key);
                if (!engine.World.TryGet(actor, out BlackboardFloatBuffer floats) || !floats.TryGet(keyId, out float value))
                {
                    return "<missing>";
                }

                return value.ToString("0.##");
            }
            catch (InvalidOperationException)
            {
                return "<missing>";
            }
        }

        private static string TryReadAimInt(GameEngine engine, Entity actor, string key)
        {
            try
            {
                return ReadAimInt(engine, actor, key).ToString();
            }
            catch (InvalidOperationException)
            {
                return "<missing>";
            }
        }

        private static int ReadAimInt(GameEngine engine, Entity actor, string key)
        {
            int keyId = ConfigKeyRegistry.GetId(key);
            if (!engine.World.TryGet(actor, out BlackboardIntBuffer ints) || !ints.TryGet(keyId, out int value))
            {
                throw new InvalidOperationException($"Blackboard int '{key}' is missing on {actor.Id}.");
            }

            return value;
        }

        private static float ReadAimFloat(GameEngine engine, Entity actor, int keyId)
        {
            if (!engine.World.TryGet(actor, out BlackboardFloatBuffer floats) || !floats.TryGet(keyId, out float value))
            {
                throw new InvalidOperationException($"Blackboard float {keyId} is missing on {actor.Id}.");
            }

            return value;
        }

        private static Entity ReadAimEntity(GameEngine engine, Entity actor, string key)
        {
            int keyId = ConfigKeyRegistry.GetId(key);
            if (!engine.World.TryGet(actor, out BlackboardEntityBuffer entities) || !entities.TryGet(keyId, out Entity value))
            {
                throw new InvalidOperationException($"Blackboard entity '{key}' is missing on {actor.Id}.");
            }

            return value;
        }

    }
}
