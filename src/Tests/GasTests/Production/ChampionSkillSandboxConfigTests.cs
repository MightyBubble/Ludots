using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Config;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.GAS.Systems;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Orders;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Input.Selection;
using Ludots.Core.Navigation2D.Components;
using Ludots.Core.Navigation2D.Systems;
using Ludots.Core.Presentation.Performers;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Projectiles;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Physics2D;
using Ludots.Core.Physics2D.Components;
using Ludots.Core.Scripting;
using Ludots.Core.UI.EntityCommandPanels;
using Ludots.Platform.Abstractions;
using Ludots.UI;
using Ludots.UI.Skia;
using NUnit.Framework;

namespace Ludots.Tests.GAS.Production
{
    [NonParallelizable]
    [TestFixture]
    public sealed class ChampionSkillSandboxConfigTests
    {
        private const float DeltaTime = 1f / 60f;
        private const string StressMapId = "champion_skill_stress";
        private const string SandboxTacticalCameraId = "ChampionSkillSandbox.Camera.Tactical";
        private const string FreeCameraToolbarButtonId = "ChampionSkillSandbox.Camera.Free";
        private const string FollowSelectionToolbarButtonId = "ChampionSkillSandbox.Camera.Selection";
        private const string FollowSelectionGroupToolbarButtonId = "ChampionSkillSandbox.Camera.SelectionGroup";
        private const string ResetCameraToolbarButtonId = "ChampionSkillSandbox.Camera.Reset";
        private const string StressTeamAIncreaseToolbarButtonId = "ChampionSkillSandbox.Stress.TeamA.Increase";
        private const string StressTeamBIncreaseToolbarButtonId = "ChampionSkillSandbox.Stress.TeamB.Increase";
        private const string StressHudBarToggleToolbarButtonId = "ChampionSkillSandbox.Stress.HudBar.Toggle";
        private const string StressHudTextToggleToolbarButtonId = "ChampionSkillSandbox.Stress.HudText.Toggle";
        private const string StressCombatTextToggleToolbarButtonId = "ChampionSkillSandbox.Stress.CombatText.Toggle";
        private const string PlayerSelectionToolbarButtonId = "ChampionSkillSandbox.Selection.Player.Live";
        private const string PlayerFormationToolbarButtonId = "ChampionSkillSandbox.Selection.Player.Formation";
        private const string AiTargetToolbarButtonId = "ChampionSkillSandbox.Selection.AI.Targets";
        private const string AiFormationToolbarButtonId = "ChampionSkillSandbox.Selection.AI.Formation";
        private const string CommandSnapshotToolbarButtonId = "ChampionSkillSandbox.Selection.Command.Snapshot";
        private static readonly string[] SandboxMods =
        {
            "LudotsCoreMod",
            "CoreInputMod",
            "CameraProfilesMod",
            "DiagnosticsOverlayMod",
            "EntityCommandPanelMod",
            "ChampionSkillSandboxMod"
        };

        [Test]
        public void ChampionSkillSandbox_TemplateConfig_LoadsJayceFormRouting()
        {
            using var engine = CreateEngine();

            var templates = new Dictionary<string, EntityTemplate>(StringComparer.OrdinalIgnoreCase);
            foreach (var template in engine.MapLoader.TemplateRegistry.GetAll())
            {
                templates[template.Id] = template;
            }

            var entity = new EntityBuilder(engine.World, templates, engine.MapLoader.PresentationAuthoringContext)
                .UseTemplate("champion_skill_sandbox_jayce")
                .Build();

            Assert.That(engine.World.Has<AbilityStateBuffer>(entity), Is.True);
            Assert.That(engine.World.Has<AbilityFormSetRef>(entity), Is.True);
            Assert.That(engine.World.Has<AbilityFormSlotBuffer>(entity), Is.True);
            Assert.That(engine.World.Has<PlayerOwner>(entity), Is.False, "Template should not hardcode scene ownership.");

            ref var abilities = ref engine.World.Get<AbilityStateBuffer>(entity);
            Assert.That(abilities.Count, Is.EqualTo(4));

            int hammerTagId = TagRegistry.GetId("State.Champion.Jayce.Hammer");
            Assert.That(hammerTagId, Is.GreaterThan(0), "Jayce hammer tag should be registered by sandbox GAS config.");

            ref var tags = ref engine.World.Get<GameplayTagContainer>(entity);
            tags.AddTag(hammerTagId);

            var routing = new AbilityFormRoutingSystem(
                engine.World,
                engine.GetService(CoreServiceKeys.AbilityFormSetRegistry),
                engine.GetService(CoreServiceKeys.TagOps));
            routing.Update(0f);

            ref var formSlots = ref engine.World.Get<AbilityFormSlotBuffer>(entity);
            var grantedSlots = default(GrantedSlotBuffer);
            int hammerQ = AbilityIdRegistry.GetId("Ability.Champion.Jayce.Hammer.ToTheSkies");
            int hammerW = AbilityIdRegistry.GetId("Ability.Champion.Jayce.Hammer.LightningField");
            int cannonR = AbilityIdRegistry.GetId("Ability.Champion.Jayce.Transform.Cannon");

            Assert.That(
                AbilitySlotResolver.Resolve(in abilities, in formSlots, hasForm: true, in grantedSlots, hasGranted: false, slotIndex: 0).AbilityId,
                Is.EqualTo(hammerQ));
            Assert.That(
                AbilitySlotResolver.Resolve(in abilities, in formSlots, hasForm: true, in grantedSlots, hasGranted: false, slotIndex: 1).AbilityId,
                Is.EqualTo(hammerW));
            Assert.That(
                AbilitySlotResolver.Resolve(in abilities, in formSlots, hasForm: true, in grantedSlots, hasGranted: false, slotIndex: 3).AbilityId,
                Is.EqualTo(cannonR));
        }

        [Test]
        public void ChampionSkillSandbox_StressTemplates_AuthorCollisionAndNavRuntimeComponents()
        {
            using var engine = CreateEngine();

            var templates = new Dictionary<string, EntityTemplate>(StringComparer.OrdinalIgnoreCase);
            foreach (var template in engine.MapLoader.TemplateRegistry.GetAll())
            {
                templates[template.Id] = template;
            }

            var warrior = new EntityBuilder(engine.World, templates, engine.MapLoader.PresentationAuthoringContext)
                .UseTemplate("champion_skill_stress_team_a_warrior")
                .Build();

            Assert.That(engine.World.Has<Collider2D>(warrior), Is.True);
            Assert.That(engine.World.Has<PhysicsMaterial2D>(warrior), Is.True);
            Assert.That(engine.World.Has<NavKinematics2D>(warrior), Is.True);

            var collider = engine.World.Get<Collider2D>(warrior);
            Assert.That(collider.Type, Is.EqualTo(ColliderType2D.Circle));
            Assert.That(ShapeDataStorage2D.TryGetCircle(collider.ShapeDataIndex, out var circle), Is.True);
            Assert.That(circle.Radius.ToFloat(), Is.EqualTo(46f).Within(0.01f));

            var physicsMaterial = engine.World.Get<PhysicsMaterial2D>(warrior);
            Assert.That(physicsMaterial.Friction.ToFloat(), Is.EqualTo(0.92f).Within(0.001f));
            Assert.That(physicsMaterial.Restitution.ToFloat(), Is.EqualTo(0f).Within(0.001f));
            Assert.That(physicsMaterial.BaseDamping.ToFloat(), Is.EqualTo(0.94f).Within(0.001f));

            var navKinematics = engine.World.Get<NavKinematics2D>(warrior);
            Assert.That(navKinematics.MaxAccelCmPerSec2.ToFloat(), Is.EqualTo(1800f).Within(0.01f));
            Assert.That(navKinematics.RadiusCm.ToFloat(), Is.EqualTo(46f).Within(0.01f));
            Assert.That(navKinematics.NeighborDistCm.ToFloat(), Is.EqualTo(320f).Within(0.01f));
            Assert.That(navKinematics.TimeHorizonSec.ToFloat(), Is.EqualTo(2.4f).Within(0.01f));
            Assert.That(navKinematics.MaxNeighbors, Is.EqualTo(20));

            var bootstrap = new NavOrderAgentBootstrapSystem(engine.World);
            bootstrap.Update(0f);

            Assert.That(engine.World.Has<NavAgent2D>(warrior), Is.True);
            Assert.That(engine.World.Has<Position2D>(warrior), Is.True);
            Assert.That(engine.World.Has<PreviousWorldPositionCm>(warrior), Is.True);
            Assert.That(engine.World.Has<PreviousPosition2D>(warrior), Is.True);
            Assert.That(engine.World.Has<Velocity2D>(warrior), Is.True);
            Assert.That(engine.World.Has<Mass2D>(warrior), Is.True);
        }

        [Test]
        public void ChampionSkillSandbox_StressMap_SpawnsCombatTeamsWithoutInitialOverlap()
        {
            using var engine = CreateEngine();
            LoadMap(engine, StressMapId, frames: 8);

            TickUntil(engine, () =>
            {
                StressCounts counts = ReadStressCounts(engine.World);
                return counts.TeamA >= 48 && counts.TeamB >= 48;
            }, maxFrames: 240);

            float teamAClearanceCm = ComputeMinimumStressTeamClearance(engine.World, StressMapId, teamId: 1);
            float teamBClearanceCm = ComputeMinimumStressTeamClearance(engine.World, StressMapId, teamId: 2);

            Assert.That(teamAClearanceCm, Is.GreaterThanOrEqualTo(0f), $"Team A should not spawn overlapped, observed clearance={teamAClearanceCm:0.##}cm.");
            Assert.That(teamBClearanceCm, Is.GreaterThanOrEqualTo(0f), $"Team B should not spawn overlapped, observed clearance={teamBClearanceCm:0.##}cm.");
        }

        [Test]
        public void ChampionSkillSandbox_StressMap_SustainsPhysicsCollisionClearanceDuringCombat()
        {
            using var engine = CreateEngine();
            LoadMap(engine, StressMapId, frames: 8);

            TickUntil(engine, () =>
            {
                StressCounts counts = ReadStressCounts(engine.World);
                return counts.TeamA >= 48 && counts.TeamB >= 48;
            }, maxFrames: 240);

            StressPhysicsTelemetry telemetry = SampleStressPhysicsTelemetry(engine, frames: 240);

            Assert.That(telemetry.PeakPhysicsStepsLastFixedTick, Is.GreaterThan(0), "Stress map should advance the Physics2D fixed-step runtime.");
            Assert.That(telemetry.PeakContactPairs, Is.GreaterThan(0), "Stress map should produce active contact pairs once both combat teams engage.");
            Assert.That(telemetry.PeakActiveCollisionPairs, Is.GreaterThan(0), "Stress map should keep collision-pair entities alive while formations clash.");
            Assert.That(telemetry.PeakProjectiles, Is.GreaterThan(0), "Stress map should remain in live combat while collision sampling runs.");
            Assert.That(
                telemetry.WorstTeamAClearanceCm,
                Is.GreaterThanOrEqualTo(-6f),
                $"Team A should keep effective body separation during combat. Observed telemetry: {telemetry}");
            Assert.That(
                telemetry.WorstTeamBClearanceCm,
                Is.GreaterThanOrEqualTo(-6f),
                $"Team B should keep effective body separation during combat. Observed telemetry: {telemetry}");
        }

        [Test]
        public void ChampionSkillSandbox_MapLoad_SeedsStateOwnershipAndPanelPresentation()
        {
            using var engine = CreateEngine();
            LoadMap(engine, "champion_skill_sandbox");

            AssertNamedEntityOwner(engine.World, "Ezreal Alpha", expectedPlayerId: 1);
            AssertNamedEntityOwner(engine.World, "Ezreal Cooldown", expectedPlayerId: 1);
            AssertNamedEntityOwner(engine.World, "Garen Alpha", expectedPlayerId: 1);
            AssertNamedEntityOwner(engine.World, "Garen Courage", expectedPlayerId: 1);
            AssertNamedEntityOwner(engine.World, "Jayce Cannon", expectedPlayerId: 1);
            AssertNamedEntityOwner(engine.World, "Jayce Hammer", expectedPlayerId: 1);

            AssertEntityHasTag(engine.World, "Ezreal Cooldown", "Cooldown.Champion.Ezreal.R");
            AssertEntityHasTag(engine.World, "Garen Courage", "State.Champion.Garen.Courage");
            AssertEntityHasTag(engine.World, "Jayce Hammer", "State.Champion.Jayce.Hammer");

            Entity selected = SelectionContextRuntime.TryGetCurrentPrimary(engine.World, engine.GlobalContext, out Entity currentPrimary)
                ? currentPrimary
                : Entity.Null;
            Assert.That(ReadEntityName(engine.World, selected), Is.EqualTo("Ezreal Alpha"), "Sandbox runtime should seed an initial controllable selection.");
            var performerRegistry = engine.GetService(CoreServiceKeys.PerformerDefinitionRegistry)
                ?? throw new InvalidOperationException("PerformerDefinitionRegistry missing.");
            Assert.That(
                performerRegistry.GetId("champion_skill_sandbox.selection_indicator"),
                Is.GreaterThan(0),
                "Sandbox performer config should register a dedicated selection indicator.");
            Assert.That(
                performerRegistry.GetId("champion_skill_sandbox.hover_indicator"),
                Is.GreaterThan(0),
                "Sandbox performer config should register a dedicated hover indicator.");
            var cameraRegistry = engine.GetService(CoreServiceKeys.VirtualCameraRegistry)
                ?? throw new InvalidOperationException("VirtualCameraRegistry missing.");
            Assert.That(
                cameraRegistry.TryGet(SandboxTacticalCameraId, out var sandboxCamera),
                Is.True,
                "Sandbox config should register a dedicated tactical camera profile.");
            Assert.That(sandboxCamera, Is.Not.Null);
            Assert.That(sandboxCamera!.ConfineTargetToWorldBounds, Is.True, "Sandbox tactical camera should clamp target to map bounds.");
            Assert.That(sandboxCamera.EdgePanRequiresPointerInsideViewport, Is.True, "Sandbox tactical camera should ignore edge pan when cursor is outside the viewport.");
            Assert.That(sandboxCamera.FollowMode, Is.EqualTo(CameraFollowMode.AlwaysFollow), "Sandbox tactical camera should allow dynamic follow targets from the toolbar.");
            Assert.That(
                engine.CurrentMapSession?.MapConfig?.DefaultCamera?.VirtualCameraId,
                Is.EqualTo(SandboxTacticalCameraId),
                "Sandbox map should use its dedicated tactical camera profile.");
            var overlays = engine.GetService(CoreServiceKeys.GroundOverlayBuffer)
                ?? throw new InvalidOperationException("GroundOverlayBuffer missing.");
            Assert.That(CountOverlays(overlays, GroundOverlayShape.Ring), Is.GreaterThan(0), "Initial sandbox selection should render a visible ring.");

            var source = ResolveGasPanelSource(engine);

            var ezrealSlots = new EntityCommandPanelSlotView[8];
            int ezrealCount = source.CopySlots(FindEntityByName(engine.World, "Ezreal Cooldown"), 0, ezrealSlots);
            Assert.That(ezrealCount, Is.EqualTo(4));
            Assert.That(ezrealSlots[3].DisplayLabel, Is.EqualTo("Trueshot Barrage"));
            Assert.That(ezrealSlots[3].ActionId, Is.EqualTo("SkillR"));
            Assert.That(ezrealSlots[3].DetailLabel, Is.EqualTo("Long line blast"));
            Assert.That(ezrealSlots[3].StateFlags.HasFlag(EntityCommandSlotStateFlags.Blocked), Is.True);

            var garenSlots = new EntityCommandPanelSlotView[8];
            int garenCount = source.CopySlots(FindEntityByName(engine.World, "Garen Courage"), 0, garenSlots);
            Assert.That(garenCount, Is.EqualTo(4));
            Assert.That(garenSlots[1].DisplayLabel, Is.EqualTo("Courage"));
            Assert.That(garenSlots[1].ActionId, Is.EqualTo("SkillW"));
            Assert.That(garenSlots[1].StateFlags.HasFlag(EntityCommandSlotStateFlags.Active), Is.True);

            var jayceSlots = new EntityCommandPanelSlotView[8];
            int jayceCount = source.CopySlots(FindEntityByName(engine.World, "Jayce Hammer"), 0, jayceSlots);
            Assert.That(jayceCount, Is.EqualTo(4));
            Assert.That(jayceSlots[0].DisplayLabel, Is.EqualTo("To The Skies!"));
            Assert.That(jayceSlots[0].ActionId, Is.EqualTo("SkillQ"));
            Assert.That(jayceSlots[0].StateFlags.HasFlag(EntityCommandSlotStateFlags.FormOverride), Is.True);
            Assert.That(jayceSlots[3].DisplayLabel, Is.EqualTo("Mercury Cannon"));
        }

        [Test]
        public void ChampionSkillSandbox_MapMoveOrder_AdvancesControllableHero()
        {
            using var engine = CreateEngine();
            LoadMap(engine, "champion_skill_sandbox");

            Entity ezreal = FindEntityByName(engine.World, "Ezreal Alpha");
            Vector2 startWorld = engine.World.Get<WorldPositionCm>(ezreal).Value.ToVector2();
            int moveToOrderTypeId = engine.MergedConfig.Constants.OrderTypeIds["moveTo"];
            var orderQueue = engine.GetService(CoreServiceKeys.OrderQueue)
                ?? throw new InvalidOperationException("OrderQueue missing.");

            var order = new Order
            {
                OrderTypeId = moveToOrderTypeId,
                PlayerId = 1,
                Actor = ezreal,
                SubmitMode = OrderSubmitMode.Immediate,
                Args = new OrderArgs
                {
                    Spatial = new OrderSpatial
                    {
                        Kind = OrderSpatialKind.WorldCm,
                        Mode = OrderCollectionMode.Single,
                        WorldCm = new Vector3(startWorld.X + 480f, 0f, startWorld.Y + 180f)
                    }
                }
            };

            Assert.That(orderQueue.TryEnqueue(in order), Is.True, "Sandbox move order should enqueue.");

            TickUntil(engine, () =>
            {
                Vector2 currentWorld = engine.World.Get<WorldPositionCm>(ezreal).Value.ToVector2();
                return Vector2.Distance(currentWorld, startWorld) > 8f;
            }, maxFrames: 30);

            Assert.That(engine.World.Has<NavAgent2D>(ezreal), Is.True, "Sandbox controllable heroes should bootstrap NavAgent2D.");
            Assert.That(engine.World.Has<Position2D>(ezreal), Is.True, "Sandbox controllable heroes should expose Position2D during movement.");

            ref var orderBuffer = ref engine.World.Get<OrderBuffer>(ezreal);
            Assert.That(orderBuffer.HasActive || orderBuffer.HasQueued || engine.World.Has<NavGoal2D>(ezreal), Is.True,
                "Move order should either stay buffered or materialize into an active nav goal while movement is in progress.");

            Vector2 endWorld = engine.World.Get<WorldPositionCm>(ezreal).Value.ToVector2();
            Assert.That(endWorld.X, Is.GreaterThan(startWorld.X + 8f));
            Assert.That(endWorld.Y, Is.GreaterThan(startWorld.Y + 4f));
        }

        [Test]
        public void ChampionSkillSandbox_CastModeToolbar_SwitchesInteractionModeAndPanelHints()
        {
            using var engine = CreateEngine();
            LoadMap(engine, "champion_skill_sandbox");

            var toolbar = engine.GetService(CoreServiceKeys.EntityCommandPanelToolbarProvider)
                ?? throw new InvalidOperationException("Toolbar provider missing.");
            var mapping = WaitForActiveInputOrderMapping(engine);
            Assert.That(toolbar.IsVisible, Is.True);
            Assert.That(mapping.InteractionMode, Is.EqualTo(InteractionModeType.SmartCast));

            var buttons = new EntityCommandPanelToolbarButtonView[5];
            int buttonCount = toolbar.CopyButtons(buttons);
            Assert.That(buttonCount, Is.EqualTo(5), "Small buffers should safely truncate the toolbar.");

            buttons = new EntityCommandPanelToolbarButtonView[8];
            buttonCount = toolbar.CopyButtons(buttons);
            Assert.That(buttonCount, Is.EqualTo(7));
            Assert.That(buttons[0].ButtonId, Is.EqualTo("ChampionSkillSandbox.Mode.SmartCast"));
            Assert.That(buttons[0].Active, Is.True);
            Assert.That(buttons[3].ButtonId, Is.EqualTo(FreeCameraToolbarButtonId));
            Assert.That(buttons[3].Active, Is.True);
            Assert.That(buttons[6].ButtonId, Is.EqualTo(ResetCameraToolbarButtonId));
            Assert.That(toolbar.Subtitle, Does.Contain("RMB Move"));

            var source = ResolveGasPanelSource(engine);
            Entity ezreal = FindEntityByName(engine.World, "Ezreal Alpha");
            var slots = new EntityCommandPanelSlotView[8];

            source.CopySlots(ezreal, 0, slots);
            Assert.That(slots[0].DetailLabel, Is.EqualTo("Line shot to cursor"));

            toolbar.Activate("ChampionSkillSandbox.Mode.Indicator");
            Tick(engine, 1);
            Assert.That(mapping.InteractionMode, Is.EqualTo(InteractionModeType.SmartCastWithIndicator));
            toolbar.CopyButtons(buttons);
            Assert.That(buttons[1].Active, Is.True);

            source.CopySlots(ezreal, 0, slots);
            Assert.That(slots[0].DetailLabel, Is.EqualTo("Hold to preview line shot"));

            toolbar.Activate("ChampionSkillSandbox.Mode.PressReleaseAim");
            Tick(engine, 1);
            Assert.That(mapping.InteractionMode, Is.EqualTo(InteractionModeType.PressReleaseAimCast));
            toolbar.CopyButtons(buttons);
            Assert.That(buttons[2].Active, Is.True);

            source.CopySlots(ezreal, 0, slots);
            Assert.That(slots[0].DetailLabel, Is.EqualTo("Release key, then confirm line shot"));

            InputOrderMapping? command = mapping.GetMapping("Command");
            Assert.That(command, Is.Not.Null);
            Assert.That(command!.OrderTypeKey, Is.EqualTo("moveTo"));
            Assert.That(command.SelectionType, Is.EqualTo(OrderSelectionType.Position));

            Entity localPlayer = engine.GetService(CoreServiceKeys.LocalPlayerEntity);
            Entity ezrealCooldown = FindEntityByName(engine.World, "Ezreal Cooldown");
            Entity garenAlpha = FindEntityByName(engine.World, "Garen Alpha");
            var selection = engine.GetService(CoreServiceKeys.SelectionRuntime)
                ?? throw new InvalidOperationException("SelectionRuntime missing.");
            selection.ReplaceSelection(localPlayer, SelectionSetKeys.LivePrimary, new[] { ezreal, garenAlpha });

            engine.World.Add(ezreal, new CameraFollowWeight { Value = 1f });
            engine.World.Add(garenAlpha, new CameraFollowWeight { Value = 3f });

            toolbar.Activate(FollowSelectionToolbarButtonId);
            Tick(engine, 2);
            toolbar.CopyButtons(buttons);
            Assert.That(buttons[3].Active, Is.False);
            Assert.That(buttons[4].Active, Is.True);

            engine.GameSession.Camera.Update(DeltaTime);
            Assert.That(engine.GameSession.Camera.FollowTargetPositionCm.HasValue, Is.True);

            toolbar.Activate(FollowSelectionGroupToolbarButtonId);
            Tick(engine, 2);
            toolbar.CopyButtons(buttons);
            Assert.That(buttons[5].Active, Is.True);
            engine.GameSession.Camera.Update(DeltaTime);
            Assert.That(engine.GameSession.Camera.FollowTargetPositionCm.HasValue, Is.True);

            Vector2 ezrealPos = engine.World.Get<WorldPositionCm>(ezreal).Value.ToVector2();
            Vector2 garenPos = engine.World.Get<WorldPositionCm>(garenAlpha).Value.ToVector2();
            Vector2 expectedGroup = (ezrealPos + (garenPos * 3f)) / 4f;
            Assert.That(engine.GameSession.Camera.FollowTargetPositionCm!.Value.X, Is.EqualTo(expectedGroup.X).Within(0.01f));
            Assert.That(engine.GameSession.Camera.FollowTargetPositionCm!.Value.Y, Is.EqualTo(expectedGroup.Y).Within(0.01f));

            toolbar.Activate(FreeCameraToolbarButtonId);
            Tick(engine, 2);
            toolbar.CopyButtons(buttons);
            Assert.That(buttons[3].Active, Is.True);
            Assert.That(engine.GameSession.Camera.FollowTargetPositionCm, Is.Null);

            engine.GameSession.Camera.ApplyPose(new CameraPoseRequest
            {
                VirtualCameraId = SandboxTacticalCameraId,
                TargetCm = new Vector2(2600f, 1480f),
                DistanceCm = 6100f,
                Pitch = 62f,
                FovYDeg = 50f,
            });
            Tick(engine, 1);

            toolbar.Activate(ResetCameraToolbarButtonId);
            Tick(engine, 4);

            Assert.That(engine.GameSession.Camera.VirtualCameraBrain?.ActiveCameraId, Is.EqualTo(SandboxTacticalCameraId));
            Assert.That(engine.GameSession.Camera.State.TargetCm.X, Is.EqualTo(1850f).Within(0.01f));
            Assert.That(engine.GameSession.Camera.State.TargetCm.Y, Is.EqualTo(980f).Within(0.01f));
            Assert.That(engine.GameSession.Camera.State.DistanceCm, Is.EqualTo(3900f).Within(0.01f));
            Assert.That(engine.GameSession.Camera.State.Pitch, Is.EqualTo(54f).Within(0.01f));
            Assert.That(engine.GameSession.Camera.State.FovYDeg, Is.EqualTo(42f).Within(0.01f));
        }

        [Test]
        public void ChampionSkillSandbox_StressMap_LoadsToolbarControlsAndMaintainsCombatFormations()
        {
            using var engine = CreateEngine();
            LoadMap(engine, StressMapId, frames: 8);

            var toolbar = engine.GetService(CoreServiceKeys.EntityCommandPanelToolbarProvider)
                ?? throw new InvalidOperationException("Toolbar provider missing.");
            var overlays = engine.GetService(CoreServiceKeys.ScreenOverlayBuffer)
                ?? throw new InvalidOperationException("ScreenOverlayBuffer missing.");

            Assert.That(toolbar.IsVisible, Is.True);
            Assert.That(toolbar.Title, Is.EqualTo("Stress Harness"));
            Assert.That(
                OverlayContainsText(overlays, "Runtime HUD | FPS="),
                Is.True,
                "Stress showcase should reuse DiagnosticsOverlayMod runtime HUD for FPS/performance readout.");

            var buttons = new EntityCommandPanelToolbarButtonView[20];
            int buttonCount = toolbar.CopyButtons(buttons);
            Assert.That(buttonCount, Is.EqualTo(19));
            Assert.That(buttons[7].ButtonId, Is.EqualTo("ChampionSkillSandbox.Stress.TeamA.Decrease"));
            Assert.That(buttons[8].ButtonId, Is.EqualTo(StressTeamAIncreaseToolbarButtonId));
            Assert.That(buttons[9].ButtonId, Is.EqualTo("ChampionSkillSandbox.Stress.TeamB.Decrease"));
            Assert.That(buttons[10].ButtonId, Is.EqualTo(StressTeamBIncreaseToolbarButtonId));
            Assert.That(buttons[11].ButtonId, Is.EqualTo(StressHudBarToggleToolbarButtonId));
            Assert.That(buttons[12].ButtonId, Is.EqualTo(StressHudTextToggleToolbarButtonId));
            Assert.That(buttons[13].ButtonId, Is.EqualTo(StressCombatTextToggleToolbarButtonId));
            Assert.That(buttons[14].ButtonId, Is.EqualTo(PlayerSelectionToolbarButtonId));
            Assert.That(buttons[15].ButtonId, Is.EqualTo(PlayerFormationToolbarButtonId));
            Assert.That(buttons[16].ButtonId, Is.EqualTo(AiTargetToolbarButtonId));
            Assert.That(buttons[17].ButtonId, Is.EqualTo(AiFormationToolbarButtonId));
            Assert.That(buttons[18].ButtonId, Is.EqualTo(CommandSnapshotToolbarButtonId));
            Assert.That(toolbar.Subtitle, Does.Contain("View P1 Live"));
            Assert.That(
                OverlayContainsText(overlays, "Selection SSOT"),
                Is.True,
                "Stress showcase should expose the active selection view panel for acceptance inspection.");

            TickUntil(engine, () =>
            {
                StressCounts counts = ReadStressCounts(engine.World);
                return counts.TeamA >= 48 &&
                       counts.TeamB >= 48 &&
                       counts.TeamAWarriors > 0 &&
                       counts.TeamAFireMages > 0 &&
                       counts.TeamALaserMages > 0 &&
                       counts.TeamAPriests > 0 &&
                       counts.TeamBWarriors > 0 &&
                       counts.TeamBFireMages > 0 &&
                       counts.TeamBLaserMages > 0 &&
                       counts.TeamBPriests > 0;
            }, maxFrames: 240);

            StressCounts saturated = ReadStressCounts(engine.World);
            Assert.That(saturated.TeamA, Is.GreaterThanOrEqualTo(48));
            Assert.That(saturated.TeamB, Is.GreaterThanOrEqualTo(48));
            Assert.That(toolbar.Subtitle, Does.Contain("Proj"));

            int peakProjectiles = SamplePeakProjectiles(engine, frames: 180);
            Assert.That(peakProjectiles, Is.GreaterThan(0), "Stress map should drive projectile combat once the formations saturate.");

            toolbar.Activate(StressTeamAIncreaseToolbarButtonId);
            toolbar.Activate(StressTeamBIncreaseToolbarButtonId);
            Tick(engine, 1);

            TickUntil(engine, () =>
            {
                StressCounts counts = ReadStressCounts(engine.World);
                return counts.TeamA >= 56 && counts.TeamB >= 56;
            }, maxFrames: 240);

            StressCounts scaled = ReadStressCounts(engine.World);
            Assert.That(scaled.TeamA, Is.GreaterThanOrEqualTo(56));
            Assert.That(scaled.TeamB, Is.GreaterThanOrEqualTo(56));

            for (int i = 0; i < 27; i++)
            {
                toolbar.Activate(StressTeamAIncreaseToolbarButtonId);
                toolbar.Activate(StressTeamBIncreaseToolbarButtonId);
            }

            TickUntil(engine, () =>
            {
                StressCounts counts = ReadStressCounts(engine.World);
                return counts.TeamA >= 272 && counts.TeamB >= 272;
            }, maxFrames: 600);

            StressCounts uncapped = ReadStressCounts(engine.World);
            Assert.That(uncapped.TeamA, Is.GreaterThanOrEqualTo(272), "Stress controls should scale beyond the old 256-unit cap.");
            Assert.That(uncapped.TeamB, Is.GreaterThanOrEqualTo(272), "Stress controls should scale beyond the old 256-unit cap.");
        }

        [Test]
        public void ChampionSkillSandbox_StressSelectionViews_SwitchViewerPerspectiveWithoutSecondaryTruth()
        {
            using var engine = CreateEngine();
            LoadMap(engine, StressMapId, frames: 8);

            var toolbar = engine.GetService(CoreServiceKeys.EntityCommandPanelToolbarProvider)
                ?? throw new InvalidOperationException("Toolbar provider missing.");
            var selection = engine.GetService(CoreServiceKeys.SelectionRuntime)
                ?? throw new InvalidOperationException("SelectionRuntime missing.");

            TickUntil(engine, () =>
            {
                StressCounts counts = ReadStressCounts(engine.World);
                return counts.TeamA >= 48 && counts.TeamB >= 48;
            }, maxFrames: 360);

            Entity localPlayer = engine.GetService(CoreServiceKeys.LocalPlayerEntity);
            Entity[] teamASelection =
            {
                FindEntityByName(engine.World, "StressFireMageA"),
                FindEntityByName(engine.World, "StressPriestA"),
                FindEntityByName(engine.World, "StressWarriorA"),
            };
            selection.ReplaceSelection(localPlayer, SelectionSetKeys.LivePrimary, teamASelection);
            Tick(engine, 2);

            toolbar.Activate(PlayerSelectionToolbarButtonId);
            Tick(engine, 2);
            Assert.That(ReadViewedSelectionNames(engine), Is.EqualTo(new[] { "StressFireMageA", "StressPriestA", "StressWarriorA" }));
            Assert.That(ReadEntityName(engine.World, SelectionContextRuntime.TryGetCurrentPrimary(engine.World, engine.GlobalContext, out Entity playerPrimary) ? playerPrimary : Entity.Null), Is.EqualTo("StressFireMageA"));

            toolbar.Activate(PlayerFormationToolbarButtonId);
            Tick(engine, 2);
            string[] playerFormation = ReadViewedSelectionNames(engine);
            Assert.That(playerFormation.Length, Is.GreaterThanOrEqualTo(48));
            Assert.That(playerFormation, Does.Contain("StressWarriorA"));
            Assert.That(playerFormation, Does.Contain("StressPriestA"));

            TickUntil(engine, () =>
            {
                toolbar.Activate(AiTargetToolbarButtonId);
                Tick(engine, 2);
                return ReadViewedSelectionNames(engine).Length > 0;
            }, maxFrames: 120);
            string[] aiTargets = ReadViewedSelectionNames(engine);
            Assert.That(aiTargets.Length, Is.GreaterThan(0));
            Assert.That(aiTargets.Any(name => name.EndsWith("A", StringComparison.Ordinal)), Is.True, "AI target selection should point at opposing team members.");

            toolbar.Activate(AiFormationToolbarButtonId);
            Tick(engine, 2);
            string[] aiFormation = ReadViewedSelectionNames(engine);
            Assert.That(aiFormation.Length, Is.GreaterThanOrEqualTo(48));
            Assert.That(aiFormation, Does.Contain("StressWarriorB"));
            Assert.That(aiFormation, Does.Contain("StressPriestB"));

            var orderQueue = engine.GetService(CoreServiceKeys.OrderQueue)
                ?? throw new InvalidOperationException("OrderQueue missing.");
            var order = new Order
            {
                OrderTypeId = engine.MergedConfig.Constants.OrderTypeIds["moveTo"],
                PlayerId = 1,
                Actor = teamASelection[0],
                SubmitMode = OrderSubmitMode.Immediate,
                Args = new OrderArgs
                {
                    Spatial = new OrderSpatial
                    {
                        Kind = OrderSpatialKind.WorldCm,
                        Mode = OrderCollectionMode.Single,
                        WorldCm = new Vector3(2200f, 0f, 1480f)
                    },
                    Selection = new OrderSelectionReference
                    {
                        Container = selection.TryCreateSnapshotLease(localPlayer, SelectionSetKeys.LivePrimary, SelectionSetKeys.CommandSnapshot, SelectionContainerKind.Snapshot, out _, out Entity snapshot)
                            ? snapshot
                            : Entity.Null
                    }
                }
            };

            Assert.That(order.Args.Selection.HasContainer, Is.True);
            Assert.That(orderQueue.TryEnqueue(in order), Is.True);
            selection.ReplaceSelection(localPlayer, SelectionSetKeys.LivePrimary, new[] { FindEntityByName(engine.World, "StressLaserMageA") });
            Tick(engine, 4);

            toolbar.Activate(CommandSnapshotToolbarButtonId);
            Tick(engine, 2);
            Assert.That(ReadViewedSelectionNames(engine), Is.EqualTo(new[] { "StressFireMageA", "StressPriestA", "StressWarriorA" }));
        }

        [Test]
        public void ChampionSkillSandbox_ProjectileBindingsAndSkillCueConfigs_AreRegistered()
        {
            using var engine = CreateEngine();

            var effects = engine.GetService(CoreServiceKeys.EffectTemplateRegistry)
                ?? throw new InvalidOperationException("EffectTemplateRegistry missing.");
            var projectileBindings = engine.GetService(CoreServiceKeys.ProjectilePresentationBindingRegistry)
                ?? throw new InvalidOperationException("ProjectilePresentationBindingRegistry missing.");
            var performers = engine.GetService(CoreServiceKeys.PerformerDefinitionRegistry)
                ?? throw new InvalidOperationException("PerformerDefinitionRegistry missing.");

            AssertProjectileEffect(
                effects,
                projectileBindings,
                performers,
                projectileEffectKey: "Effect.Champion.Ezreal.MysticShot",
                resolveEffectKey: "Effect.Champion.Ezreal.MysticShotResolve",
                projectilePerformerKey: "champion_skill_sandbox.projectile.ezreal_q");
            AssertProjectileEffect(
                effects,
                projectileBindings,
                performers,
                projectileEffectKey: "Effect.Champion.Ezreal.TrueshotBarrage",
                resolveEffectKey: "Effect.Champion.Ezreal.TrueshotBarrageResolve",
                projectilePerformerKey: "champion_skill_sandbox.projectile.ezreal_r");
            AssertProjectileEffect(
                effects,
                projectileBindings,
                performers,
                projectileEffectKey: "Effect.Champion.Jayce.Cannon.ShockBlast",
                resolveEffectKey: "Effect.Champion.Jayce.Cannon.ShockBlastResolve",
                projectilePerformerKey: "champion_skill_sandbox.projectile.jayce_q");
            AssertProjectileEffect(
                effects,
                projectileBindings,
                performers,
                projectileEffectKey: "Effect.ChampionStress.FireMage.Fireball",
                resolveEffectKey: "Effect.ChampionStress.FireMage.FireballResolve",
                projectilePerformerKey: "champion_skill_sandbox.projectile.stress_fireball");
            AssertProjectileEffect(
                effects,
                projectileBindings,
                performers,
                projectileEffectKey: "Effect.ChampionStress.LaserMage.Laser",
                resolveEffectKey: "Effect.ChampionStress.LaserMage.LaserResolve",
                projectilePerformerKey: "champion_skill_sandbox.projectile.stress_laser");

            Assert.That(performers.GetId("champion_skill_sandbox.cue.ezreal_arcane_shift"), Is.GreaterThan(0));
            Assert.That(performers.GetId("champion_skill_sandbox.cue.ezreal_essence_flux_cast"), Is.GreaterThan(0));
            Assert.That(performers.GetId("champion_skill_sandbox.cue.ezreal_essence_flux_hit"), Is.GreaterThan(0));
            Assert.That(performers.GetId("champion_skill_sandbox.cue.garen_courage"), Is.GreaterThan(0));
            Assert.That(performers.GetId("champion_skill_sandbox.cue.garen_demacian_justice_hit"), Is.GreaterThan(0));
            Assert.That(performers.GetId("champion_skill_sandbox.cue.jayce_hammer_lightning_field"), Is.GreaterThan(0));
            Assert.That(performers.GetId("champion_skill_sandbox.cue.jayce_transform_hammer"), Is.GreaterThan(0));
            Assert.That(performers.GetId("champion_skill_sandbox.cue.stress_warrior_cleave"), Is.GreaterThan(0));
            Assert.That(performers.GetId("champion_skill_sandbox.cue.stress_fireball_cast"), Is.GreaterThan(0));
            Assert.That(performers.GetId("champion_skill_sandbox.cue.stress_fireball_hit"), Is.GreaterThan(0));
            Assert.That(performers.GetId("champion_skill_sandbox.cue.stress_laser_cast"), Is.GreaterThan(0));
            Assert.That(performers.GetId("champion_skill_sandbox.cue.stress_laser_hit"), Is.GreaterThan(0));
            Assert.That(performers.GetId("champion_skill_sandbox.cue.stress_priest_heal_cast"), Is.GreaterThan(0));
            Assert.That(performers.GetId("champion_skill_sandbox.cue.stress_priest_heal_hit"), Is.GreaterThan(0));
        }

        private static GameEngine CreateEngine()
        {
            string repoRoot = FindRepoRoot();
            string assetsRoot = Path.Combine(repoRoot, "assets");
            var modPaths = RepoModPaths.ResolveExplicit(repoRoot, SandboxMods);

            var engine = new GameEngine();
            engine.InitializeWithConfigPipeline(modPaths, assetsRoot);
            InstallInput(engine);
            InstallUi(engine);
            engine.Start();
            return engine;
        }

        private static void InstallInput(GameEngine engine)
        {
            var inputConfig = new InputConfigPipelineLoader(engine.ConfigPipeline).Load();
            var backend = new NullInputBackend();
            var inputHandler = new PlayerInputHandler(backend, inputConfig);
            for (int i = 0; i < engine.MergedConfig.StartupInputContexts.Count; i++)
            {
                inputHandler.PushContext(engine.MergedConfig.StartupInputContexts[i]);
            }

            engine.SetService(CoreServiceKeys.InputHandler, inputHandler);
            engine.SetService(CoreServiceKeys.InputBackend, (IInputBackend)backend);
            engine.SetService(CoreServiceKeys.UiCaptured, false);
        }

        private static void InstallUi(GameEngine engine)
        {
            var uiRoot = new UIRoot(new SkiaUiRenderer());
            uiRoot.Resize(1920f, 1080f);
            engine.SetService(CoreServiceKeys.UIRoot, uiRoot);
            engine.SetService(CoreServiceKeys.UiTextMeasurer, (object)new SkiaTextMeasurer());
            engine.SetService(CoreServiceKeys.UiImageSizeProvider, (object)new SkiaImageSizeProvider());
        }

        private static void LoadMap(GameEngine engine, string mapId, int frames = 12)
        {
            engine.LoadMap(mapId);
            Assert.That(engine.CurrentMapSession, Is.Not.Null, $"{mapId} should create a live map session.");
            Tick(engine, frames);
            Assert.That(engine.TriggerManager.Errors.Count, Is.EqualTo(0), "Sandbox map should load without trigger errors.");
        }

        private static InputOrderMappingSystem WaitForActiveInputOrderMapping(GameEngine engine, int maxFrames = 24)
        {
            for (int i = 0; i < maxFrames; i++)
            {
                var mapping = engine.GetService(CoreServiceKeys.ActiveInputOrderMapping);
                if (mapping != null)
                {
                    return mapping;
                }

                Tick(engine, 1);
            }

            throw new InvalidOperationException("Active input order mapping missing.");
        }

        private static void Tick(GameEngine engine, int frames)
        {
            for (int i = 0; i < frames; i++)
            {
                engine.Tick(DeltaTime);
            }
        }

        private static void TickUntil(GameEngine engine, Func<bool> predicate, int maxFrames)
        {
            for (int i = 0; i < maxFrames; i++)
            {
                if (predicate())
                {
                    return;
                }

                Tick(engine, 1);
            }

            Assert.That(predicate(), Is.True, $"Predicate was not satisfied within {maxFrames} frames.");
        }

        private static int CountOverlays(GroundOverlayBuffer overlays, GroundOverlayShape shape)
        {
            int count = 0;
            foreach (ref readonly var item in overlays.GetSpan())
            {
                if (item.Shape == shape)
                {
                    count++;
                }
            }

            return count;
        }

        private static bool OverlayContainsText(ScreenOverlayBuffer overlay, string expected)
        {
            foreach (ref readonly var item in overlay.GetSpan())
            {
                if (item.Kind != ScreenOverlayItemKind.Text)
                {
                    continue;
                }

                string? text = overlay.GetString(item.StringId);
                if (!string.IsNullOrEmpty(text) &&
                    text.Contains(expected, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static int SamplePeakProjectiles(GameEngine engine, int frames)
        {
            int peak = 0;
            for (int i = 0; i < frames; i++)
            {
                Tick(engine, 1);
                peak = Math.Max(peak, CountProjectiles(engine.World));
            }

            return peak;
        }

        private static int CountProjectiles(World world)
        {
            int count = 0;
            var query = new QueryDescription().WithAll<ProjectileState>();
            world.Query(in query, (Entity _, ref ProjectileState __) => count++);
            return count;
        }

        private static StressPhysicsTelemetry SampleStressPhysicsTelemetry(GameEngine engine, int frames)
        {
            float worstTeamAClearanceCm = float.PositiveInfinity;
            float worstTeamBClearanceCm = float.PositiveInfinity;
            int peakProjectiles = 0;
            int peakContactPairs = 0;
            int peakActiveCollisionPairs = 0;
            int peakPhysicsStepsLastFixedTick = 0;

            for (int i = 0; i < frames; i++)
            {
                Tick(engine, 1);

                Physics2DPerfStats stats = ReadPhysicsPerfStats(engine.World);
                peakPhysicsStepsLastFixedTick = Math.Max(peakPhysicsStepsLastFixedTick, stats.PhysicsStepsLastFixedTick);
                peakContactPairs = Math.Max(peakContactPairs, stats.ContactPairs);
                peakActiveCollisionPairs = Math.Max(peakActiveCollisionPairs, CountActiveCollisionPairs(engine.World));
                peakProjectiles = Math.Max(peakProjectiles, CountProjectiles(engine.World));
                worstTeamAClearanceCm = Math.Min(worstTeamAClearanceCm, ComputeMinimumStressTeamClearance(engine.World, StressMapId, teamId: 1));
                worstTeamBClearanceCm = Math.Min(worstTeamBClearanceCm, ComputeMinimumStressTeamClearance(engine.World, StressMapId, teamId: 2));
            }

            return new StressPhysicsTelemetry(
                worstTeamAClearanceCm,
                worstTeamBClearanceCm,
                peakProjectiles,
                peakContactPairs,
                peakActiveCollisionPairs,
                peakPhysicsStepsLastFixedTick);
        }

        private static Physics2DPerfStats ReadPhysicsPerfStats(World world)
        {
            var query = new QueryDescription().WithAll<Physics2DPerfStats>();
            Physics2DPerfStats stats = default;
            bool found = false;
            world.Query(in query, (Entity _, ref Physics2DPerfStats value) =>
            {
                if (found)
                {
                    return;
                }

                stats = value;
                found = true;
            });

            Assert.That(found, Is.True, "Physics2DPerfStats should be published while the stress map is running.");
            return stats;
        }

        private static int CountActiveCollisionPairs(World world)
        {
            int count = 0;
            var query = new QueryDescription().WithAll<CollisionPair, ActiveCollisionPairTag>();
            world.Query(in query, (Entity _, ref CollisionPair __, ref ActiveCollisionPairTag ___) => count++);
            return count;
        }

        private static StressCounts ReadStressCounts(World world)
        {
            int teamA = 0;
            int teamB = 0;
            int teamAWarriors = 0;
            int teamAFireMages = 0;
            int teamALaserMages = 0;
            int teamAPriests = 0;
            int teamBWarriors = 0;
            int teamBFireMages = 0;
            int teamBLaserMages = 0;
            int teamBPriests = 0;

            var query = new QueryDescription().WithAll<Name, Team, MapEntity, AbilityStateBuffer>();
            world.Query(in query, (Entity _, ref Name name, ref Team team, ref MapEntity mapEntity, ref AbilityStateBuffer __) =>
            {
                if (!string.Equals(mapEntity.MapId.Value, StressMapId, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                if (team.Id == 1)
                {
                    teamA++;
                    if (name.Value.Contains("FireMage", StringComparison.Ordinal))
                    {
                        teamAFireMages++;
                    }
                    else if (name.Value.Contains("LaserMage", StringComparison.Ordinal))
                    {
                        teamALaserMages++;
                    }
                    else if (name.Value.Contains("Priest", StringComparison.Ordinal))
                    {
                        teamAPriests++;
                    }
                    else
                    {
                        teamAWarriors++;
                    }
                }
                else if (team.Id == 2)
                {
                    teamB++;
                    if (name.Value.Contains("FireMage", StringComparison.Ordinal))
                    {
                        teamBFireMages++;
                    }
                    else if (name.Value.Contains("LaserMage", StringComparison.Ordinal))
                    {
                        teamBLaserMages++;
                    }
                    else if (name.Value.Contains("Priest", StringComparison.Ordinal))
                    {
                        teamBPriests++;
                    }
                    else
                    {
                        teamBWarriors++;
                    }
                }
            });

            return new StressCounts(
                teamA,
                teamB,
                teamAWarriors,
                teamAFireMages,
                teamALaserMages,
                teamAPriests,
                teamBWarriors,
                teamBFireMages,
                teamBLaserMages,
                teamBPriests);
        }

        private static float ComputeMinimumStressTeamClearance(World world, string mapId, int teamId)
        {
            var units = new List<StressBodySample>(128);
            var query = new QueryDescription().WithAll<Team, MapEntity, AbilityStateBuffer, WorldPositionCm, Collider2D>();
            world.Query(in query, (Entity _, ref Team team, ref MapEntity mapEntity, ref AbilityStateBuffer __, ref WorldPositionCm position, ref Collider2D collider) =>
            {
                if (team.Id != teamId ||
                    !string.Equals(mapEntity.MapId.Value, mapId, StringComparison.OrdinalIgnoreCase) ||
                    collider.Type != ColliderType2D.Circle ||
                    !ShapeDataStorage2D.TryGetCircle(collider.ShapeDataIndex, out var circle))
                {
                    return;
                }

                units.Add(new StressBodySample(position.Value.ToVector2(), circle.Radius.ToFloat()));
            });

            float minimumClearanceCm = float.MaxValue;
            for (int i = 0; i < units.Count; i++)
            {
                StressBodySample a = units[i];
                for (int j = i + 1; j < units.Count; j++)
                {
                    StressBodySample b = units[j];
                    float distanceCm = Vector2.Distance(a.PositionCm, b.PositionCm);
                    float clearanceCm = distanceCm - (a.RadiusCm + b.RadiusCm);
                    if (clearanceCm < minimumClearanceCm)
                    {
                        minimumClearanceCm = clearanceCm;
                    }
                }
            }

            return minimumClearanceCm == float.MaxValue ? float.PositiveInfinity : minimumClearanceCm;
        }

        private static void AssertProjectileEffect(
            EffectTemplateRegistry effects,
            ProjectilePresentationBindingRegistry projectileBindings,
            PerformerDefinitionRegistry performers,
            string projectileEffectKey,
            string resolveEffectKey,
            string projectilePerformerKey)
        {
            int projectileEffectId = EffectTemplateIdRegistry.GetId(projectileEffectKey);
            int resolveEffectId = EffectTemplateIdRegistry.GetId(resolveEffectKey);
            int projectilePerformerId = performers.GetId(projectilePerformerKey);

            Assert.That(projectileEffectId, Is.GreaterThan(0), $"{projectileEffectKey} should be registered.");
            Assert.That(resolveEffectId, Is.GreaterThan(0), $"{resolveEffectKey} should be registered.");
            Assert.That(projectilePerformerId, Is.GreaterThan(0), $"{projectilePerformerKey} should be registered.");

            Assert.That(effects.TryGet(projectileEffectId, out var projectileEffect), Is.True);
            Assert.That(projectileEffect.PresetType, Is.EqualTo(EffectPresetType.LaunchProjectile));
            Assert.That(projectileEffect.Projectile.ImpactEffectTemplateId, Is.EqualTo(resolveEffectId));

            Assert.That(effects.TryGet(resolveEffectId, out var resolveEffect), Is.True);
            Assert.That(resolveEffect.PresetType, Is.EqualTo(EffectPresetType.Search));

            Assert.That(projectileBindings.TryGet(resolveEffectId, out var binding), Is.True);
            Assert.That(binding.ImpactEffectTemplateId, Is.EqualTo(resolveEffectId));
            Assert.That(binding.StartupPerformers.Count, Is.EqualTo(1));
            Assert.That(binding.StartupPerformers.Get(0), Is.EqualTo(projectilePerformerId));
        }

        private static IEntityCommandPanelSource ResolveGasPanelSource(GameEngine engine)
        {
            var registry = engine.GetService(CoreServiceKeys.EntityCommandPanelSourceRegistry)
                ?? throw new InvalidOperationException("EntityCommandPanelSourceRegistry missing.");
            Assert.That(registry.TryGet("gas.ability-slots", out IEntityCommandPanelSource source), Is.True);
            return source;
        }

        private static Entity FindEntityByName(World world, string entityName)
        {
            Entity found = Entity.Null;
            var query = new QueryDescription().WithAll<Name>();
            world.Query(in query, (Entity entity, ref Name name) =>
            {
                if (found != Entity.Null)
                {
                    return;
                }

                if (string.Equals(name.Value, entityName, StringComparison.Ordinal))
                {
                    found = entity;
                }
            });

            Assert.That(found, Is.Not.EqualTo(Entity.Null), $"Entity '{entityName}' should exist on champion_skill_sandbox.");
            return found;
        }

        private static string ReadEntityName(World world, Entity entity)
        {
            return entity != Entity.Null && world.IsAlive(entity) && world.TryGet(entity, out Name name)
                ? name.Value
                : string.Empty;
        }

        private static string[] ReadViewedSelectionNames(GameEngine engine)
        {
            Entity[] selected = SelectionContextRuntime.SnapshotCurrentSelection(engine.World, engine.GlobalContext);
            return selected.Select(entity => ReadEntityName(engine.World, entity))
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .ToArray();
        }

        private static void AssertNamedEntityOwner(World world, string entityName, int expectedPlayerId)
        {
            Entity found = FindEntityByName(world, entityName);
            Assert.That(world.TryGet(found, out PlayerOwner owner), Is.True, $"{entityName} should have PlayerOwner.");
            Assert.That(owner.PlayerId, Is.EqualTo(expectedPlayerId), $"{entityName} should receive PlayerOwner from map instance overrides.");
        }

        private static void AssertEntityHasTag(World world, string entityName, string tagName)
        {
            Entity found = FindEntityByName(world, entityName);
            int tagId = TagRegistry.GetId(tagName);
            Assert.That(tagId, Is.GreaterThan(0), $"{tagName} should be registered.");
            Assert.That(world.TryGet(found, out GameplayTagContainer tags), Is.True, $"{entityName} should carry gameplay tags.");
            Assert.That(tags.HasTag(tagId), Is.True, $"{entityName} should contain tag {tagName}.");
        }

        private static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (int i = 0; i < 10 && dir != null; i++)
            {
                string srcDir = Path.Combine(dir.FullName, "src");
                string assetsDir = Path.Combine(dir.FullName, "assets");
                if (Directory.Exists(srcDir) && Directory.Exists(assetsDir))
                {
                    return dir.FullName;
                }

                dir = dir.Parent;
            }

            throw new DirectoryNotFoundException("Failed to locate repository root from test output directory.");
        }

        private readonly record struct StressCounts(
            int TeamA,
            int TeamB,
            int TeamAWarriors,
            int TeamAFireMages,
            int TeamALaserMages,
            int TeamAPriests,
            int TeamBWarriors,
            int TeamBFireMages,
            int TeamBLaserMages,
            int TeamBPriests);

        private readonly record struct StressPhysicsTelemetry(
            float WorstTeamAClearanceCm,
            float WorstTeamBClearanceCm,
            int PeakProjectiles,
            int PeakContactPairs,
            int PeakActiveCollisionPairs,
            int PeakPhysicsStepsLastFixedTick);

        private readonly record struct StressBodySample(Vector2 PositionCm, float RadiusCm);

        private sealed class NullInputBackend : IInputBackend
        {
            public float GetAxis(string devicePath) => 0f;
            public bool GetButton(string devicePath) => false;
            public System.Numerics.Vector2 GetMousePosition() => System.Numerics.Vector2.Zero;
            public float GetMouseWheel() => 0f;
            public void EnableIME(bool enable) { }
            public void SetIMECandidatePosition(int x, int y) { }
            public string GetCharBuffer() => string.Empty;
        }
    }
}
