using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Scripting;
using Ludots.UI;
using Ludots.UI.Compose;
using Ludots.UI.Runtime;
using Ludots.UI.Surface;

namespace RtsCncFullShowcaseMod.Systems;

internal sealed class RtsCncFullMatchHudSystem : ISystem<float>
{
    private readonly GameEngine _engine;
    private readonly RtsCncFullMatchRuntime _runtime;
    private IUiSurfaceHost? _surfaceHost;
    private UiSurfaceLeaseHandle _lease;
    private int _publishedRevision = -1;
    private float _elapsedSincePublish;
    private bool _disposed;

    public RtsCncFullMatchHudSystem(GameEngine engine, RtsCncFullMatchRuntime runtime)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
    }

    public void Initialize() { }
    public void BeforeUpdate(in float dt) { }
    public void AfterUpdate(in float dt) { }

    public void Update(in float dt)
    {
        if (_disposed ||
            !string.Equals(_engine.CurrentMapSession?.MapId.Value, "rts_cnc_full", StringComparison.Ordinal))
        {
            return;
        }

        EnsureLease();
        _elapsedSincePublish += Math.Clamp(dt, 0f, 0.1f);
        if (_surfaceHost == null ||
            (_publishedRevision == _runtime.Revision && _elapsedSincePublish < 0.15f))
        {
            return;
        }

        RtsCncFullMatchView snapshot = _runtime.ToView();
        _surfaceHost.Publish(
            _lease,
            UiSurfaceContribution.FromBuilder(() => BuildRoot(snapshot)));
        _publishedRevision = _runtime.Revision;
        _elapsedSincePublish = 0f;
    }

    public void Dispose()
    {
        _disposed = true;
        if (_surfaceHost != null && _lease.IsValid)
        {
            _surfaceHost.ReleaseLease(ref _lease);
        }
    }

    private void EnsureLease()
    {
        if (_lease.IsValid && _surfaceHost != null)
        {
            return;
        }

        _surfaceHost = _engine.GetService(CoreServiceKeys.UiSurfaceHost) as IUiSurfaceHost;
        if (_surfaceHost == null)
        {
            return;
        }

        _lease = _surfaceHost.Acquire(new UiSurfaceLeaseRequest(
            "RtsCncFull.MatchHud",
            UiSurfaceSegment.Overlay,
            priority: 180,
            exclusive: false));
    }

    private UiElementBuilder BuildRoot(RtsCncFullMatchView state)
    {
        return Ui.Panel(
                BuildCommandPanel(state),
                BuildBattlePanel(state))
            .WidthPercent(100f)
            .HeightPercent(100f);
    }

    private UiElementBuilder BuildCommandPanel(RtsCncFullMatchView state)
    {
        return Ui.Column(
                Ui.Text("C&C Full RTS")
                    .FontSize(26f)
                    .Bold()
                    .Color(UiColor.White),
                Ui.Text("DataPlane match control")
                    .FontSize(13f)
                    .Color(UiColor.LightGray),
                Ui.Row(
                        BuildChip("Credits", state.Credits.ToString("0")),
                        BuildChip("Ore", state.Ore.ToString("0")),
                        BuildChip("Rate", $"{state.HarvestRate:0}/s"))
                    .Gap(8f)
                    .Wrap(),
                Ui.Row(
                        BuildActionButton("1 Mine", RtsCncFullMatchAction.StartHarvest, CanClickMine(state)),
                        BuildActionButton("2 Train", RtsCncFullMatchAction.TrainArmy, CanClickTrain(state)),
                        BuildActionButton("3 Attack", RtsCncFullMatchAction.AttackEnemy, CanClickAttack(state)))
                    .Gap(8f)
                    .Wrap(),
                BuildActionButton("Reset", RtsCncFullMatchAction.Reset, true)
                    .Width(116f),
                Ui.Text(state.PhaseLabel)
                    .FontSize(20f)
                    .Bold()
                    .Color(state.Victory ? UiColor.Gold : UiColor.White),
                Ui.Text($"{MathF.Round(state.PhaseProgress * 100f):0}% - {state.Instruction}")
                    .FontSize(14f)
                    .Color(UiColor.LightGray),
                Ui.Text($"Last input: {state.LastInput}")
                    .FontSize(13f)
                    .Color("#9FE2FF"),
                Ui.Text($"Last event: {state.LastEvent}")
                    .FontSize(13f)
                    .Color("#C6F6D5"))
            .Width(454f)
            .Padding(16f)
            .Gap(10f)
            .Background(new UiColor(5, 12, 18, 224))
            .Outline(2f, state.Victory ? UiColor.Gold : new UiColor(74, 144, 226, 220))
            .Absolute(20f, 20f)
            .ZIndex(500);
    }

    private UiElementBuilder BuildBattlePanel(RtsCncFullMatchView state)
    {
        return Ui.Column(
                Ui.Text("Live ECS loop")
                    .FontSize(18f)
                    .Bold()
                    .Color(UiColor.White),
                Ui.Row(
                        BuildChip("Harvest loads", state.HarvestLoads.ToString()),
                        BuildChip("New units", state.UnitsTrained.ToString()),
                        BuildChip("Army", state.PlayerArmyAlive.ToString()))
                    .Gap(8f)
                    .Wrap(),
                Ui.Row(
                        BuildChip("Enemy units", state.EnemyUnitsAlive.ToString()),
                        BuildChip("Enemy base", state.EnemyStructuresAlive.ToString()),
                        BuildChip("Destroyed", state.EnemyDestroyed.ToString()))
                    .Gap(8f)
                    .Wrap(),
                Ui.Column(state.Log.Select(line =>
                        Ui.Text(line)
                            .FontSize(12f)
                            .Color(UiColor.LightGray))
                    .ToArray())
                    .Gap(4f))
            .Width(560f)
            .Padding(14f)
            .Gap(8f)
            .Background(new UiColor(10, 10, 12, 210))
            .Outline(1f, new UiColor(190, 190, 190, 150))
            .Absolute(20f, 624f)
            .ZIndex(500);
    }

    private UiElementBuilder BuildChip(string label, string value)
    {
        return Ui.Column(
                Ui.Text(label)
                    .FontSize(11f)
                    .Color(UiColor.LightGray),
                Ui.Text(value)
                    .FontSize(18f)
                    .Bold()
                    .Color(UiColor.White))
            .Width(132f)
            .Padding(8f)
            .Gap(2f)
            .Background(new UiColor(28, 36, 45, 230))
            .Outline(1f, new UiColor(88, 102, 118, 180));
    }

    private UiElementBuilder BuildActionButton(string label, RtsCncFullMatchAction action, bool enabled)
    {
        return Ui.Button(label, _ =>
            {
                _runtime.TryQueueAction(action, "surface-ui", out string _);
            })
            .FontSize(15f)
            .Padding(12f, 10f)
            .Radius(6f)
            .Width(138f)
            .Background(enabled ? new UiColor(35, 116, 171, 240) : new UiColor(48, 54, 60, 210))
            .Color(enabled ? UiColor.White : UiColor.LightGray);
    }

    private static bool CanClickMine(RtsCncFullMatchView state)
    {
        return string.Equals(state.Phase, nameof(RtsCncFullMatchPhase.AwaitingInput), StringComparison.Ordinal);
    }

    private static bool CanClickTrain(RtsCncFullMatchView state)
    {
        return string.Equals(state.Phase, nameof(RtsCncFullMatchPhase.ReadyToTrain), StringComparison.Ordinal);
    }

    private static bool CanClickAttack(RtsCncFullMatchView state)
    {
        return string.Equals(state.Phase, nameof(RtsCncFullMatchPhase.ReadyToAttack), StringComparison.Ordinal);
    }
}
