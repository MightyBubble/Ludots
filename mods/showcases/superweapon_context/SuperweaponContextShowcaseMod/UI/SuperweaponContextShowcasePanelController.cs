using System;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.EntityCollections;
using Ludots.Core.Input.CommandSources;
using Ludots.Core.Scripting;
using Ludots.UI;
using Ludots.UI.Compose;
using Ludots.UI.Reactive;
using Ludots.UI.Runtime;
using Ludots.UI.Surface;
using SuperweaponContextShowcaseMod.Runtime;

namespace SuperweaponContextShowcaseMod.UI
{
    internal sealed class SuperweaponContextShowcasePanelController
    {
        private readonly SuperweaponContextShowcaseState _runtimeState;
        private ReactivePage<PanelState>? _page;
        private UiSurfaceLeaseHandle _lease;
        private GameEngine? _engine;

        public SuperweaponContextShowcasePanelController(SuperweaponContextShowcaseState runtimeState)
        {
            _runtimeState = runtimeState ?? throw new ArgumentNullException(nameof(runtimeState));
        }

        public void MountOrRefresh(GameEngine engine)
        {
            if (engine.GetService(CoreServiceKeys.UiSurfaceHost) is not IUiSurfaceHost surfaceHost ||
                !_runtimeState.IsActive)
            {
                Clear();
                return;
            }

            _engine = engine;
            PanelState state = BuildState(engine);
            if (_page == null)
            {
                var textMeasurer = (IUiTextMeasurer)engine.GetService(CoreServiceKeys.UiTextMeasurer);
                var imageSizeProvider = (IUiImageSizeProvider)engine.GetService(CoreServiceKeys.UiImageSizeProvider);
                _page = new ReactivePage<PanelState>(textMeasurer, imageSizeProvider, state, BuildRoot);
            }
            else if (!_page.State.Equals(state))
            {
                _page.SetState(_ => state);
            }

            surfaceHost.PublishReactivePage(
                ref _lease,
                new UiSurfaceLeaseRequest("Showcase.SuperweaponContext.Panel", UiSurfaceSegment.Overlay, priority: 55),
                _page);
        }

        public void Clear()
        {
            if (_lease.IsValid &&
                _engine?.GetService(CoreServiceKeys.UiSurfaceHost) is IUiSurfaceHost surfaceHost)
            {
                surfaceHost.ReleaseLease(ref _lease);
            }

            _engine = null;
        }

        private UiElementBuilder BuildRoot(ReactiveContext<PanelState> context)
        {
            PanelState state = context.State;
            return Ui.Column(BuildPanel(state))
                .WidthPercent(100f)
                .HeightPercent(100f)
                .Absolute(0f, 0f)
                .ZIndex(55);
        }

        private static UiElementBuilder BuildPanel(PanelState state)
        {
            return Ui.Column(
                    Ui.Text("Superweapon Targeting")
                        .FontSize(24f)
                        .Bold()
                        .Color("#F5F7FA"),
                    Ui.Text("The ability temporarily owns the target list")
                        .FontSize(12f)
                        .Color("#B8C4D4"),
                    BuildScenarioLine("Given", $"{state.CommanderName} starts with {state.ActiveGroupSummary} ready"),
                    BuildScenarioLine("When", "Ability opens Superweapon Targeting and writes its own target list"),
                    BuildScenarioLine("Then", $"{state.RoutedTargetCount} target(s) locked: {state.TargetSummary}; {ResolveConfirmSummary(state)}"),
                    BuildTargetRoute(state),
                    Ui.Row(
                            BuildChip("targeting", state.ContextActive ? "active" : (state.ConfirmEventPublished ? "restored" : "waiting"), state.ContextActive || state.ConfirmEventPublished),
                            BuildChip("targets", state.RoutedTargetCount.ToString(System.Globalization.CultureInfo.InvariantCulture), state.RoutedTargetCount > 0),
                            BuildChip("confirm", state.ConfirmEventPublished ? "complete" : "pending", state.ConfirmEventPublished))
                        .Wrap()
                        .Gap(8f),
                    Ui.Text("Enter / LMB confirm    Esc / RMB cancel")
                        .FontSize(12f)
                        .Color("#D7E1EC"))
                .Width(488f)
                .Padding(14f)
                .Gap(9f)
                .Radius(8f)
                .Background("#071019")
                .Absolute(16f, 16f);
        }

        private static UiElementBuilder BuildTargetRoute(PanelState state)
        {
            bool routed = state.RoutedTargetCount >= 2;
            return Ui.Card(
                    Ui.Text("Visible flow")
                        .FontSize(11f)
                        .Bold()
                        .Color("#F0C36B"),
                    Ui.Row(
                            BuildRouteNode("Commander", "#6EC6FF", active: true),
                            BuildArrow("opens targeting"),
                            BuildRouteNode("Target List", routed ? "#9EE493" : "#4E6173", routed),
                            BuildArrow("routes targets"),
                            Ui.Column(
                                    BuildRouteNode("Arcweaver", "#F6D37A", routed),
                                    BuildRouteNode("Vanguard", "#F6D37A", routed))
                                .Gap(6f),
                            BuildArrow("waits for confirm"),
                            BuildRouteNode(state.ConfirmEventPublished ? "Confirmed" : "Confirm Pending", state.ConfirmEventPublished ? "#9EE493" : "#D7E1EC", active: true))
                        .Align(UiAlignItems.Center)
                        .Gap(8f)
                        .Wrap())
                .Gap(7f)
                .Padding(10f)
                .Radius(8f)
                .Background("#101A24");
        }

        private static UiElementBuilder BuildRouteNode(string label, string accentColor, bool active)
        {
            return Ui.Text(label)
                .FontSize(12f)
                .Bold()
                .Color(active ? "#071019" : "#C7D0DD")
                .Background(active ? accentColor : "#203142")
                .Padding(10f, 7f)
                .Radius(8f);
        }

        private static UiElementBuilder BuildArrow(string label)
        {
            return Ui.Text($"-> {label}")
                .FontSize(11f)
                .Color("#B8C4D4")
                .Width(86f)
                .WhiteSpace(UiWhiteSpace.Normal);
        }

        private static UiElementBuilder BuildScenarioLine(string label, string text)
        {
            return Ui.Card(
                    Ui.Text(label)
                        .FontSize(11f)
                        .Bold()
                        .Color("#F0C36B"),
                    Ui.Text(text)
                        .FontSize(13f)
                        .Color("#F5F7FA")
                        .WhiteSpace(UiWhiteSpace.Normal))
                .Gap(4f)
                .Padding(10f)
                .Radius(8f)
                .Background("#101A24");
        }

        private static UiElementBuilder BuildChip(string label, string value, bool active)
        {
            return Ui.Text($"{label}: {value}")
                .FontSize(12f)
                .Color(active ? "#071019" : "#C7D0DD")
                .Background(active ? "#9EE493" : "#142230")
                .Padding(8f, 6f)
                .Radius(8f);
        }

        private static string ResolveConfirmSummary(PanelState state)
        {
            return state.ConfirmEventPublished
                ? "confirm is complete"
                : "confirm is pending";
        }

        private PanelState BuildState(GameEngine engine)
        {
            return new PanelState(
                CommanderName: ResolveName(engine, _runtimeState.Commander),
                ActiveGroupSummary: ResolveActiveGroupSummary(engine),
                TargetSummary: ResolveTargetSummary(engine),
                RoutedTargetCount: _runtimeState.RoutedTargetCount,
                ContextActive: HasActiveSuperweaponFrame(engine),
                ConfirmInputObserved: _runtimeState.ConfirmInputObserved,
                ConfirmEventPublished: _runtimeState.ConfirmEventPublished,
                ConfirmEventCount: _runtimeState.ConfirmEventCount,
                Revision: _runtimeState.Revision);
        }

        private bool HasActiveSuperweaponFrame(GameEngine engine)
        {
            if (engine.GetService(CoreServiceKeys.InteractionContextStack) is not Ludots.Core.Input.Interaction.InteractionContextStack stack ||
                !stack.TryPeek(out Ludots.Core.Input.Interaction.InteractionContextFrame frame))
            {
                return false;
            }

            return frame.ContextEntity == _runtimeState.Commander &&
                   frame.ContextId == stack.ContextIdRegistry.GetId(SuperweaponContextShowcaseIds.ContextProfileId);
        }

        private static string ResolveName(GameEngine engine, Entity entity)
        {
            return entity != Entity.Null &&
                   engine.World.IsAlive(entity) &&
                   engine.World.TryGet(entity, out Name name)
                ? name.Value
                : "(missing)";
        }

        private string ResolveTargetSummary(GameEngine engine)
        {
            string first = ResolveName(engine, _runtimeState.Arcweaver);
            string second = ResolveName(engine, _runtimeState.Vanguard);
            return $"{first} + {second}";
        }

        private static string ResolveActiveGroupSummary(GameEngine engine)
        {
            int count = TryResolveLocalCommandSourceOwner(engine, out Entity owner)
                ? EntityCollectionContextRuntime.GetCount(
                    engine.GlobalContext,
                    owner,
                    EntityCollectionKeys.CommandSource)
                : 0;
            if (count <= 0)
            {
                return "no active heroes";
            }

            return $"{count} active hero(es)";
        }

        private static bool TryResolveLocalCommandSourceOwner(GameEngine engine, out Entity owner)
        {
            owner = Entity.Null;
            Entity local = engine.GetService(CoreServiceKeys.LocalPlayerEntity);
            if (local == Entity.Null || !engine.World.IsAlive(local))
            {
                return false;
            }

            owner = local;
            return true;
        }

        private sealed record PanelState(
            string CommanderName,
            string ActiveGroupSummary,
            string TargetSummary,
            int RoutedTargetCount,
            bool ContextActive,
            bool ConfirmInputObserved,
            bool ConfirmEventPublished,
            int ConfirmEventCount,
            uint Revision);
    }
}
