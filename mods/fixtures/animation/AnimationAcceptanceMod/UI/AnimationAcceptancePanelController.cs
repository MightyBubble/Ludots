using System;
using System.Collections.Generic;
using Arch.Core;
using AnimationAcceptanceMod.Runtime;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Scripting;
using Ludots.UI;
using Ludots.UI.Compose;
using Ludots.UI.Reactive;
using Ludots.UI.Runtime;

namespace AnimationAcceptanceMod.UI
{
    internal sealed class AnimationAcceptancePanelController
    {
        private const float PanelWidth = 556f;
        private const float PanelHeight = 688f;
        private static readonly QueryDescription RigQuery = new QueryDescription()
            .WithAll<Name, VisualRuntimeState, AnimatorPackedState, AnimatorRuntimeState, AnimatorParameterBuffer, AnimatorAuxState>();

        private readonly ReactivePage<AnimationAcceptancePanelState> _page;
        private GameEngine? _engine;

        public AnimationAcceptancePanelController(IUiTextMeasurer textMeasurer, IUiImageSizeProvider imageSizeProvider)
        {
            _page = new ReactivePage<AnimationAcceptancePanelState>(
                textMeasurer,
                imageSizeProvider,
                AnimationAcceptancePanelState.Empty,
                BuildRoot);
        }

        public UiScene Scene => _page.Scene;

        public void MountOrSync(UIRoot root, GameEngine engine)
        {
            ArgumentNullException.ThrowIfNull(root);
            ArgumentNullException.ThrowIfNull(engine);

            _engine = engine;
            if (!ReferenceEquals(root.Scene, _page.Scene))
            {
                root.MountScene(_page.Scene);
            }

            _page.SetState(_ => CaptureState(engine));
            root.IsDirty = true;
        }

        public void ClearIfOwned(UIRoot root)
        {
            ArgumentNullException.ThrowIfNull(root);

            if (ReferenceEquals(root.Scene, _page.Scene))
            {
                root.ClearScene();
            }

            _engine = null;
            _page.SetState(_ => AnimationAcceptancePanelState.Empty);
        }

        private UiElementBuilder BuildRoot(ReactiveContext<AnimationAcceptancePanelState> context)
        {
            AnimationAcceptancePanelState state = context.State;
            if (string.IsNullOrWhiteSpace(state.MapId))
            {
                return Ui.Card(
                        Ui.Text("Animator Acceptance Inspector").FontSize(22f).Bold().Color("#F7FAFF"),
                        Ui.Text("No active animation acceptance scene.").FontSize(13f).Color("#9DB0C6"))
                    .Width(420f)
                    .Padding(16f)
                    .Gap(10f)
                    .Radius(18f)
                    .Background("#09121C")
                    .Absolute(16f, 16f)
                    .ZIndex(30);
            }

            AnimationAcceptanceRigPanelState rig = ResolveSelectedRig(state);
            var content = new List<UiElementBuilder>
            {
                BuildHeader(state),
                BuildOverviewSection(state),
                BuildRigSwitchSection(state),
                BuildRigCard(rig),
            };

            return Ui.Column(
                    Ui.Text(" ")
                        .WidthPercent(100f)
                        .HeightPercent(100f)
                        .Absolute(0f, 0f)
                        .Background("#02060A12")
                        .ZIndex(34),
                    Ui.ScrollView(content.ToArray())
                        .Width(PanelWidth)
                        .Height(PanelHeight)
                        .Padding(14f)
                        .Gap(12f)
                        .Radius(20f)
                        .Background("#07111CDD")
                        .Border(1f, ParseColor("#335B748D"))
                        .BoxShadow(0f, 14f, 32f, ParseColor("#70000000"))
                        .Absolute(708f, 16f)
                        .ZIndex(40))
                .WidthPercent(100f)
                .HeightPercent(100f)
                .Absolute(0f, 0f);
        }

        private UiElementBuilder BuildHeader(AnimationAcceptancePanelState state)
        {
            return Ui.Card(
                    Ui.Row(
                            Ui.Column(
                                    Ui.Text("Animator Acceptance Inspector").FontSize(22f).Bold().Color("#F7FAFF"),
                    Ui.Text("Core lower-body state machine + adapter-side builtin clip surrogate prototype.")
                                        .FontSize(12f)
                                        .Color("#B5C4D4")
                                        .WhiteSpace(UiWhiteSpace.Normal))
                                .Gap(4f)
                                .FlexGrow(1f),
                            Ui.Text("LIVE")
                                .FontSize(10f)
                                .Bold()
                                .Color("#F7FAFF")
                                .Padding(8f, 4f)
                                .Radius(999f)
                                .Background("#2A5A7A"))
                        .Justify(UiJustifyContent.SpaceBetween)
                        .Align(UiAlignItems.Center),
                    Ui.Text($"Map: {state.MapId}").FontSize(12f).Color("#86A0BA"),
                    Ui.Text("Use the rig switcher to inspect one case at a time. Auto shows the canned path; Manual lets you drive state-machine parameters and watch packed/runtime/aux ownership in place.")
                        .FontSize(12f)
                        .Color("#D7E2EE")
                        .WhiteSpace(UiWhiteSpace.Normal))
                .Padding(14f)
                .Gap(8f)
                .Radius(18f)
                .Background("#0D1A28");
        }

        private UiElementBuilder BuildOverviewSection(AnimationAcceptancePanelState state)
        {
            var primerLines = new List<UiElementBuilder>();
            for (int i = 0; i < state.PrimerLines.Length; i++)
            {
                primerLines.Add(
                    Ui.Text(state.PrimerLines[i])
                        .FontSize(10f)
                        .Color("#9DB0C6")
                        .WhiteSpace(UiWhiteSpace.Normal));
            }

            return Ui.Card(
                    Ui.Row(
                            Ui.Column(
                                    Ui.Text("Global Playback").FontSize(12f).Bold().Color("#F3C87E"),
                                    Ui.Text($"Current playback scale: {state.PlaybackScale:0.00}x").FontSize(11f).Color("#A7BACD"))
                                .Gap(4f)
                                .FlexGrow(1f),
                            Ui.Column(
                                    Ui.Text("Acceptance Primer").FontSize(12f).Bold().Color("#F3C87E"),
                                    Ui.Column(primerLines.ToArray()).Gap(4f))
                                .Width(252f)
                                .Gap(4f))
                        .Gap(12f)
                        .Align(UiAlignItems.Start),
                    Ui.Row(
                            BuildActionButton("0.5x", Approximately(state.PlaybackScale, 0.5f), () => SetPlaybackScale(0.5f)),
                            BuildActionButton("1.0x", Approximately(state.PlaybackScale, 1f), () => SetPlaybackScale(1f)),
                            BuildActionButton("1.5x", Approximately(state.PlaybackScale, 1.5f), () => SetPlaybackScale(1.5f)),
                            BuildActionButton("2.0x", Approximately(state.PlaybackScale, 2f), () => SetPlaybackScale(2f)))
                        .Wrap()
                        .Gap(8f))
                .Padding(12f)
                .Gap(8f)
                .Radius(16f)
                .Background("#0C1724");
        }

        private UiElementBuilder BuildRigSwitchSection(AnimationAcceptancePanelState state)
        {
            var cards = new List<UiElementBuilder>();
            for (int i = 0; i < state.Rigs.Length; i++)
            {
                AnimationAcceptanceRigPanelState rig = state.Rigs[i];
                bool active = rig.RigId == state.SelectedRig;
                string label = active
                    ? $"{rig.DisplayName} | FOCUS | {rig.AnimatorLines[0]}"
                    : $"{rig.DisplayName} | {rig.DriverModeLabel} | {rig.AnimatorLines[0]}";
                cards.Add(
                    Ui.Button(label, _ => SetSelectedRig(rig.RigId))
                        .Padding(10f)
                        .Radius(14f)
                        .Background(active ? "#173149" : "#0B1723")
                        .Border(1f, ParseColor(active ? "#AAE8C26C" : "#33495F75")));
            }

            return Ui.Card(
                    Ui.Text("Rig Switcher").FontSize(12f).Bold().Color("#F3C87E"),
                    Ui.Row(cards.ToArray()).Wrap().Gap(8f))
                .Padding(12f)
                .Gap(8f)
                .Radius(16f)
                .Background("#0C1724");
        }

        private UiElementBuilder BuildRigCard(AnimationAcceptanceRigPanelState rig)
        {
            return Ui.Card(
                    Ui.Row(
                            Ui.Column(
                                    Ui.Text(rig.DisplayName).FontSize(17f).Bold().Color("#F7FAFF"),
                                    Ui.Text(rig.EntityName).FontSize(11f).Color("#87A5C0"),
                                    Ui.Text(rig.Summary).FontSize(11f).Color("#D7E2EE").WhiteSpace(UiWhiteSpace.Normal))
                                .Gap(3f)
                                .FlexGrow(1f),
                            Ui.Column(
                                    BuildModeBadge(rig.DriverModeLabel, rig.DriverMode == AnimationAcceptanceDriverMode.Auto ? "#295A6B" : "#7A5428"),
                                    Ui.Text(rig.ActiveProfileLabel).FontSize(10f).Color("#F3C87E"))
                                .Gap(4f)
                                .Align(UiAlignItems.End))
                        .Justify(UiJustifyContent.SpaceBetween)
                        .Align(UiAlignItems.Start),
                    Ui.Text(rig.LayerSummary).FontSize(11f).Color("#8FA6BD").WhiteSpace(UiWhiteSpace.Normal),
                    BuildControlSection(rig),
                    BuildRuntimeSection(rig),
                    BuildConfigSection(rig))
                .Padding(14f)
                .Gap(10f)
                .Radius(18f)
                .Background("#102030")
                .Border(1f, ParseColor("#284055"));
        }

        private UiElementBuilder BuildControlSection(AnimationAcceptanceRigPanelState rig)
        {
            return Ui.Column(
                    Ui.Text("Controls").FontSize(12f).Bold().Color("#F3C87E"),
                    Ui.Row(
                            BuildActionButton("Auto", rig.DriverMode == AnimationAcceptanceDriverMode.Auto, () => SetDriverMode(rig.RigId, AnimationAcceptanceDriverMode.Auto)),
                            BuildActionButton("Manual", rig.DriverMode == AnimationAcceptanceDriverMode.Manual, () => SetDriverMode(rig.RigId, AnimationAcceptanceDriverMode.Manual)))
                        .Gap(8f),
                    Ui.Row(BuildProfileButtons(rig)).Wrap().Gap(8f),
                    BuildStepperRow("Speed", rig.SpeedText,
                        BuildActionButton("-0.10", false, () => StepSpeed(rig.RigId, -0.10f)),
                        BuildActionButton("+0.10", false, () => StepSpeed(rig.RigId, 0.10f)),
                        BuildActionButton(rig.MoveEnabled ? "Move On" : "Move Off", rig.MoveEnabled, () => ToggleMove(rig.RigId))),
                    BuildStepperRow("Facing", rig.FacingText,
                        BuildActionButton("Left", false, () => StepFacing(rig.RigId, -0.35f)),
                        BuildActionButton("Center", false, () => SetFacing(rig.RigId, 0f)),
                        BuildActionButton("Right", false, () => StepFacing(rig.RigId, 0.35f))),
                    BuildStepperRow("Aim", rig.AimText,
                        BuildActionButton("Left", false, () => StepAim(rig.RigId, -0.25f)),
                        BuildActionButton("Center", false, () => SetAim(rig.RigId, 0f)),
                        BuildActionButton("Right", false, () => StepAim(rig.RigId, 0.25f))),
                    BuildStepperRow("Overlay", rig.OverlayWeightText,
                        BuildActionButton("-0.10", false, () => StepOverlayWeight(rig.RigId, -0.10f)),
                        BuildActionButton("+0.10", false, () => StepOverlayWeight(rig.RigId, 0.10f)),
                        BuildActionButton("Fire Trigger", false, () => TriggerFire(rig.RigId))))
                .Gap(8f);
        }

        private UiElementBuilder BuildRuntimeSection(AnimationAcceptanceRigPanelState rig)
        {
            return Ui.Column(
                    Ui.Text("Runtime Snapshot").FontSize(12f).Bold().Color("#F3C87E"),
                    BuildInfoCard("Animator", rig.AnimatorLines),
                    BuildInfoCard("Parameters", rig.ParameterLines),
                    BuildInfoCard("Aux Layer", rig.AuxLines))
                .Gap(8f);
        }

        private UiElementBuilder BuildConfigSection(AnimationAcceptanceRigPanelState rig)
        {
            return Ui.Column(
                    Ui.Text("Sample Config").FontSize(12f).Bold().Color("#F3C87E"),
                    BuildInfoCard("States", rig.StateLines),
                    BuildInfoCard("Builtin Clips", rig.BuiltinLines),
                    BuildInfoCard("Transitions", rig.TransitionLines))
                .Gap(8f);
        }

        private UiElementBuilder[] BuildProfileButtons(AnimationAcceptanceRigPanelState rig)
        {
            var buttons = new List<UiElementBuilder>();
            for (int i = 0; i < rig.Profiles.Length; i++)
            {
                var profile = rig.Profiles[i];
                buttons.Add(
                    Ui.Button(profile.Label, _ => ApplyProfile(rig.RigId, profile.Id))
                        .Padding(8f, 6f)
                        .Radius(999f)
                        .Background(profile.Active ? "#6D4A1F" : "#152436")
                        .Border(1f, ParseColor(profile.Active ? "#AAE8C26C" : "#33495F75"))
                        .Color(profile.Active ? "#FFF8E7" : "#C7D7E6"));
            }

            return buttons.ToArray();
        }

        private static UiElementBuilder BuildStepperRow(string label, string value, params UiElementBuilder[] actions)
        {
            return Ui.Column(
                    Ui.Row(
                            Ui.Text(label).FontSize(11f).Bold().Color("#D9E5F2"),
                            Ui.Text(value).FontSize(11f).Color("#87A5C0"))
                        .Justify(UiJustifyContent.SpaceBetween),
                    Ui.Row(actions).Wrap().Gap(8f))
                .Gap(6f)
                .Padding(10f)
                .Radius(14f)
                .Background("#0B1723");
        }

        private static UiElementBuilder BuildInfoCard(string title, string[] lines)
        {
            var children = new List<UiElementBuilder>
            {
                Ui.Text(title).FontSize(11f).Bold().Color("#D9E5F2"),
            };

            for (int i = 0; i < lines.Length; i++)
            {
                children.Add(
                    Ui.Text(lines[i])
                        .FontSize(10f)
                        .Color("#93AAC0")
                        .WhiteSpace(UiWhiteSpace.Normal));
            }

            return Ui.Column(children.ToArray())
                .Gap(5f)
                .Padding(10f)
                .Radius(14f)
                .Background("#0B1723");
        }

        private static UiElementBuilder BuildModeBadge(string text, string background)
        {
            return Ui.Text(text)
                .FontSize(10f)
                .Bold()
                .Color("#F7FAFF")
                .Padding(8f, 4f)
                .Radius(999f)
                .Background(background);
        }

        private static UiElementBuilder BuildActionButton(string label, bool active, Action onClick)
        {
            return Ui.Button(label, _ => onClick())
                .Padding(9f, 7f)
                .Radius(999f)
                .Background(active ? "#315976" : "#152436")
                .Border(1f, ParseColor(active ? "#88C7DFFF" : "#33495F75"))
                .Color(active ? "#F7FAFF" : "#C7D7E6");
        }

        private AnimationAcceptancePanelState CaptureState(GameEngine engine)
        {
            string mapId = engine.CurrentMapSession?.MapId.Value ?? string.Empty;
            if (!string.Equals(mapId, AnimationAcceptanceIds.StartupMapId, StringComparison.OrdinalIgnoreCase))
            {
                return AnimationAcceptancePanelState.Empty;
            }

            var controls = RequireControls(engine);
            var registry = engine.GetService(CoreServiceKeys.AnimatorControllerRegistry)
                ?? throw new InvalidOperationException("Animation acceptance requires AnimatorControllerRegistry.");

            var samples = ResolveRigSamples(engine, registry);
            var rigs = new AnimationAcceptanceRigPanelState[AnimationAcceptanceRigCatalog.All.Length];
            for (int i = 0; i < AnimationAcceptanceRigCatalog.All.Length; i++)
            {
                var definition = AnimationAcceptanceRigCatalog.All[i];
                var slot = controls.GetSlot(definition.RigId);
                if (!samples.TryGetValue(definition.RigId, out var sample))
                {
                    sample = RigRuntimeSample.Empty;
                }

                rigs[i] = BuildRigPanelState(definition, slot, sample);
            }

            return new AnimationAcceptancePanelState(
                MapId: mapId,
                PlaybackScale: controls.PlaybackScale,
                SelectedRig: controls.SelectedRig,
                PrimerLines:
                [
                    "Tank verifies locomotion_cycle + aim_yaw_offset + recoil_pulse atoms on a vehicle surrogate.",
                    "Humanoid verifies the same builtin atoms on a biped surrogate with walk/run lower-body transitions.",
                    "Packed state, runtime state, parameter buffer, and builtin clip payload are shown together so ownership is inspectable frame by frame.",
                ],
                Rigs: rigs);
        }

        private static Dictionary<AnimationAcceptanceRigId, RigRuntimeSample> ResolveRigSamples(GameEngine engine, AnimatorControllerRegistry registry)
        {
            var controllerLookup = new Dictionary<int, AnimationAcceptanceRigDefinition>();
            for (int i = 0; i < AnimationAcceptanceRigCatalog.All.Length; i++)
            {
                int controllerId = registry.GetId(AnimationAcceptanceRigCatalog.All[i].ControllerKey);
                if (controllerId > 0)
                {
                    controllerLookup[controllerId] = AnimationAcceptanceRigCatalog.All[i];
                }
            }

            var result = new Dictionary<AnimationAcceptanceRigId, RigRuntimeSample>();
            var query = engine.World.Query(in RigQuery);
            foreach (var chunk in query)
            {
                var names = chunk.GetArray<Name>();
                var visuals = chunk.GetArray<VisualRuntimeState>();
                var packedStates = chunk.GetArray<AnimatorPackedState>();
                var runtimeStates = chunk.GetArray<AnimatorRuntimeState>();
                var parameterBuffers = chunk.GetArray<AnimatorParameterBuffer>();
                var auxStates = chunk.GetArray<AnimatorAuxState>();

                for (int i = 0; i < chunk.Count; i++)
                {
                    if (!controllerLookup.TryGetValue(visuals[i].AnimatorControllerId, out var definition))
                    {
                        continue;
                    }

                    result[definition.RigId] = new RigRuntimeSample(
                        Found: true,
                        EntityName: names[i].Value ?? definition.DisplayName,
                        PackedState: packedStates[i],
                        RuntimeState: runtimeStates[i],
                        Parameters: parameterBuffers[i],
                        AuxState: auxStates[i]);
                }
            }

            return result;
        }

        private static AnimationAcceptanceRigPanelState BuildRigPanelState(
            AnimationAcceptanceRigDefinition definition,
            AnimationAcceptanceRigControlSlot slot,
            RigRuntimeSample sample)
        {
            string currentState = ResolveStateLabel(definition.StateLabels, sample.RuntimeState.CurrentStateIndex);
            string nextState = sample.RuntimeState.NextStateIndex == AnimatorRuntimeState.NoState
                ? "None"
                : ResolveStateLabel(definition.StateLabels, sample.RuntimeState.NextStateIndex);
            string[] animatorLines =
            [
                $"current = {currentState}",
                $"next = {nextState}",
                $"normalized = {sample.PackedState.GetNormalizedTime01():0.000}  transition = {sample.PackedState.GetTransitionProgress01():0.000}",
                $"elapsed = {sample.RuntimeState.StateElapsedSeconds:0.000}s  blend = {sample.RuntimeState.TransitionElapsedSeconds:0.000}/{sample.RuntimeState.TransitionDurationSeconds:0.000}s",
                $"packed primary = {sample.PackedState.GetPrimaryStateIndex()}  secondary = {sample.PackedState.GetSecondaryStateIndex()}  flags = {sample.PackedState.GetFlags()}",
            ];

            var parameterLines = new List<string>();
            for (int i = 0; i < definition.FloatParameters.Length; i++)
            {
                var parameter = definition.FloatParameters[i];
                parameterLines.Add($"{parameter.Label}[{parameter.Index}] = {sample.Parameters.GetFloat(parameter.Index):0.00}  ({parameter.Description})");
            }
            for (int i = 0; i < definition.BoolParameters.Length; i++)
            {
                var parameter = definition.BoolParameters[i];
                parameterLines.Add($"{parameter.Label}[{parameter.Index}] = {sample.Parameters.GetBool(parameter.Index)}  ({parameter.Description})");
            }
            for (int i = 0; i < definition.TriggerParameters.Length; i++)
            {
                var parameter = definition.TriggerParameters[i];
                parameterLines.Add($"{parameter.Label}[{parameter.Index}] pending = {sample.Parameters.HasTrigger(parameter.Index)}  ({parameter.Description})");
            }

            string[] auxLines =
            [
                BuildClipLine("base", sample.AuxState.BaseClip),
                BuildClipLine("layer", sample.AuxState.LayerClip),
                BuildClipLine("overlay", sample.AuxState.OverlayClip),
            ];

            string[] stateLines = BuildStateLines(definition);
            string[] builtinLines = definition.BuiltinClipDescriptions;
            var profiles = new AnimationAcceptanceProfilePanelState[definition.Profiles.Length];
            for (int i = 0; i < definition.Profiles.Length; i++)
            {
                var profile = definition.Profiles[i];
                profiles[i] = new AnimationAcceptanceProfilePanelState(
                    Id: profile.Id,
                    Label: profile.Label,
                    Summary: profile.Summary,
                    Active: string.Equals(profile.Id, slot.ActiveProfileId, StringComparison.OrdinalIgnoreCase));
            }

            return new AnimationAcceptanceRigPanelState(
                RigId: definition.RigId,
                DisplayName: definition.DisplayName,
                EntityName: string.IsNullOrWhiteSpace(sample.EntityName) ? definition.DisplayName : sample.EntityName,
                Summary: definition.Summary,
                LayerSummary: definition.LayerSummary,
                DriverMode: slot.DriverMode,
                DriverModeLabel: slot.DriverMode == AnimationAcceptanceDriverMode.Auto ? "AUTO DRIVER" : "MANUAL DRIVER",
                ActiveProfileLabel: $"profile: {slot.ActiveProfileId}",
                SpeedText: $"{slot.Speed:0.00}",
                FacingText: $"{ToDegrees(slot.FacingYawRad):0.0} deg",
                AimText: $"{ToDegrees(slot.AimYawRad):0.0} deg",
                OverlayWeightText: $"{slot.OverlayWeight01:0.00}",
                MoveEnabled: slot.MoveEnabled,
                AnimatorLines: animatorLines,
                ParameterLines: parameterLines.ToArray(),
                AuxLines: auxLines,
                StateLines: stateLines,
                BuiltinLines: builtinLines,
                TransitionLines: definition.TransitionDescriptions,
                Profiles: profiles);
        }

        private static string[] BuildStateLines(AnimationAcceptanceRigDefinition definition)
        {
            var lines = new string[definition.StateLabels.Length];
            for (int i = 0; i < definition.StateLabels.Length; i++)
            {
                string description = i < definition.StateDescriptions.Length ? definition.StateDescriptions[i] : string.Empty;
                lines[i] = $"[{i}] {definition.StateLabels[i]} : {description}";
            }

            return lines;
        }

        private static string ResolveStateLabel(string[] labels, int stateIndex)
        {
            if ((uint)stateIndex < (uint)labels.Length)
            {
                return $"[{stateIndex}] {labels[stateIndex]}";
            }

            return stateIndex < 0 ? "None" : $"[{stateIndex}] Unknown";
        }

        private static string BuildClipLine(string slotLabel, AnimatorBuiltinClipState clip)
        {
            if (!clip.IsActive)
            {
                return $"{slotLabel} = None";
            }

            string scalar0 = clip.ClipId == AnimatorBuiltinClipId.AimYawOffset
                ? $"{ToDegrees(clip.Scalar0):0.0} deg"
                : $"{clip.Scalar0:0.00}";

            return $"{slotLabel} = {clip.ClipId}  time = {clip.NormalizedTime01:0.000}  weight = {clip.Weight01:0.00}  s0 = {scalar0}";
        }

        private static UiColor ParseColor(string hex)
        {
            return UiColor.TryParse(hex, out var color) ? color : UiColor.White;
        }

        private static float ToDegrees(float radians)
        {
            return radians * (180f / MathF.PI);
        }

        private static bool Approximately(float left, float right)
        {
            return MathF.Abs(left - right) <= 0.01f;
        }

        private void SetPlaybackScale(float scale)
        {
            RequireControls().SetPlaybackScale(scale);
            SyncMountedRoot();
        }

        private void SetDriverMode(AnimationAcceptanceRigId rigId, AnimationAcceptanceDriverMode mode)
        {
            RequireControls().SetDriverMode(rigId, mode);
            SyncMountedRoot();
        }

        private void ApplyProfile(AnimationAcceptanceRigId rigId, string profileId)
        {
            RequireControls().ApplyProfile(rigId, profileId);
            SyncMountedRoot();
        }

        private void StepSpeed(AnimationAcceptanceRigId rigId, float delta)
        {
            RequireControls().StepSpeed(rigId, delta);
            SyncMountedRoot();
        }

        private void ToggleMove(AnimationAcceptanceRigId rigId)
        {
            RequireControls().ToggleMove(rigId);
            SyncMountedRoot();
        }

        private void StepFacing(AnimationAcceptanceRigId rigId, float delta)
        {
            RequireControls().StepFacing(rigId, delta);
            SyncMountedRoot();
        }

        private void SetFacing(AnimationAcceptanceRigId rigId, float yawRad)
        {
            RequireControls().SetFacing(rigId, yawRad);
            SyncMountedRoot();
        }

        private void StepAim(AnimationAcceptanceRigId rigId, float delta)
        {
            RequireControls().StepAim(rigId, delta);
            SyncMountedRoot();
        }

        private void SetAim(AnimationAcceptanceRigId rigId, float yawRad)
        {
            RequireControls().SetAim(rigId, yawRad);
            SyncMountedRoot();
        }

        private void StepOverlayWeight(AnimationAcceptanceRigId rigId, float delta)
        {
            RequireControls().StepOverlayWeight(rigId, delta);
            SyncMountedRoot();
        }

        private void SetSelectedRig(AnimationAcceptanceRigId rigId)
        {
            RequireControls().SetSelectedRig(rigId);
            SyncMountedRoot();
        }

        private void TriggerFire(AnimationAcceptanceRigId rigId)
        {
            RequireControls().TriggerFire(rigId);
            SyncMountedRoot();
        }

        private static AnimationAcceptanceRigPanelState ResolveSelectedRig(AnimationAcceptancePanelState state)
        {
            for (int i = 0; i < state.Rigs.Length; i++)
            {
                if (state.Rigs[i].RigId == state.SelectedRig)
                {
                    return state.Rigs[i];
                }
            }

            if (state.Rigs.Length == 0)
            {
                throw new InvalidOperationException("AnimationAcceptancePanelState requires at least one rig.");
            }

            return state.Rigs[0];
        }

        private void SyncMountedRoot()
        {
            GameEngine engine = RequireEngine();
            if (engine.GetService(CoreServiceKeys.UIRoot) is not UIRoot root ||
                !ReferenceEquals(root.Scene, _page.Scene))
            {
                return;
            }

            _page.SetState(_ => CaptureState(engine));
            root.IsDirty = true;
        }

        private GameEngine RequireEngine()
        {
            return _engine ?? throw new InvalidOperationException("AnimationAcceptancePanelController is not bound to an engine.");
        }

        private AnimationAcceptanceControlState RequireControls()
        {
            return RequireControls(RequireEngine());
        }

        private static AnimationAcceptanceControlState RequireControls(GameEngine engine)
        {
            return engine.GetService(AnimationAcceptanceServiceKeys.ControlState)
                ?? throw new InvalidOperationException("Animation acceptance control state is not installed.");
        }

        private sealed record AnimationAcceptancePanelState(
            string MapId,
            float PlaybackScale,
            AnimationAcceptanceRigId SelectedRig,
            string[] PrimerLines,
            AnimationAcceptanceRigPanelState[] Rigs)
        {
            public static readonly AnimationAcceptancePanelState Empty = new(
                string.Empty,
                1f,
                AnimationAcceptanceRigId.Tank,
                Array.Empty<string>(),
                Array.Empty<AnimationAcceptanceRigPanelState>());
        }

        private sealed record AnimationAcceptanceRigPanelState(
            AnimationAcceptanceRigId RigId,
            string DisplayName,
            string EntityName,
            string Summary,
            string LayerSummary,
            AnimationAcceptanceDriverMode DriverMode,
            string DriverModeLabel,
            string ActiveProfileLabel,
            string SpeedText,
            string FacingText,
            string AimText,
            string OverlayWeightText,
            bool MoveEnabled,
            string[] AnimatorLines,
            string[] ParameterLines,
            string[] AuxLines,
            string[] StateLines,
            string[] BuiltinLines,
            string[] TransitionLines,
            AnimationAcceptanceProfilePanelState[] Profiles);

        private sealed record AnimationAcceptanceProfilePanelState(
            string Id,
            string Label,
            string Summary,
            bool Active);

        private readonly record struct RigRuntimeSample(
            bool Found,
            string EntityName,
            AnimatorPackedState PackedState,
            AnimatorRuntimeState RuntimeState,
            AnimatorParameterBuffer Parameters,
            AnimatorAuxState AuxState)
        {
            public static readonly RigRuntimeSample Empty = new(
                false,
                string.Empty,
                default,
                AnimatorRuntimeState.Create(0),
                default,
                default);
        }
    }
}
