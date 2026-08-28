using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Arch.Core;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using Ludots.UI;
using Ludots.UI.Compose;
using Ludots.UI.Reactive;
using Ludots.UI.Runtime;
using Ludots.UI.Surface;
using ReconnectRecoveryShowcaseMod.Runtime;

namespace ReconnectRecoveryShowcaseMod;

internal static class ReconnectRecoveryShowcaseIds
{
    public const string MapId = "reconnect_recovery";
    public const string InstalledKey = "ReconnectRecoveryShowcase.Installed";
    public const string RuntimeKey = "ReconnectRecoveryShowcase.Runtime";
    public const string HeroName = "Save Hero";
}

internal sealed class ReconnectTickSystem : BaseSystem<World, float>
{
    private readonly ReconnectRecoveryRuntime _runtime;
    public ReconnectTickSystem(GameEngine engine, ReconnectRecoveryRuntime runtime) : base(engine.World) { _runtime = runtime; }
    public override void Update(in float dt) => _runtime.Tick();
}

internal sealed class ReconnectPresentationSystem : BaseSystem<World, float>
{
    private readonly GameEngine _engine;
    private readonly ReconnectRecoveryRuntime _runtime;
    private readonly ReconnectPanel _panel;

    public ReconnectPresentationSystem(GameEngine engine, ReconnectRecoveryRuntime runtime) : base(engine.World)
    {
        _engine = engine;
        _runtime = runtime;
        _panel = new ReconnectPanel(runtime);
    }

    public override void Update(in float dt)
    {
        if (!_runtime.IsShowcaseMap) { _panel.ClearIfOwned(); return; }
        _panel.MountOrRefresh(_engine);
    }
}

internal sealed class ReconnectPanel
{
    private readonly ReconnectRecoveryRuntime _runtime;
    private ReactivePage<ReconnectState>? _page;
    private GameEngine? _engine;
    private UiSurfaceLeaseHandle _lease;

    public ReconnectPanel(ReconnectRecoveryRuntime runtime) => _runtime = runtime;

    public void MountOrRefresh(GameEngine engine)
    {
        if (engine.GetService(CoreServiceKeys.UiSurfaceHost) is not IUiSurfaceHost host) return;
        _engine = engine;
        ReconnectState state = _runtime.BuildState();
        if (_page == null)
        {
            var text = (IUiTextMeasurer)engine.GetService(CoreServiceKeys.UiTextMeasurer);
            var images = (IUiImageSizeProvider)engine.GetService(CoreServiceKeys.UiImageSizeProvider);
            _page = new ReactivePage<ReconnectState>(text, images, state, BuildRoot);
        }
        else if (!_page.State.Equals(state)) _page.SetState(_ => state);
        host.PublishReactivePage(ref _lease, new UiSurfaceLeaseRequest("ReconnectRecoveryShowcase.Panel", UiSurfaceSegment.Overlay, priority: 55), _page);
    }

    public void ClearIfOwned()
    {
        if (_lease.IsValid && _engine?.GetService(CoreServiceKeys.UiSurfaceHost) is IUiSurfaceHost host) host.ReleaseLease(ref _lease);
        _engine = null;
    }

    private UiElementBuilder BuildRoot(ReactiveContext<ReconnectState> context)
    {
        ReconnectState state = context.State;
        int gap = state.AuthorityTick - state.ClientTick;
        string gapLine = state.Disconnected
            ? $"DISCONNECTED — authority ran {gap} tick(s) ahead while the client froze"
            : "connected — both timelines advance together";
        return Ui.Column(
            Ui.Column(
                Ui.Text("Reconnect Recovery — resume from the authority").FontSize(20f).Bold().Color("#F5F7FA"),
                Ui.Text("Disconnecting is not a reset: the authoritative world keeps ticking, the client freezes, and reconnecting resumes from the authoritative checkpoint — never from a local illusion. (Single-machine simulation; the true network fault injection path is not yet accepted — see design doc.)")
                    .FontSize(11f).Color("#D6E0EA").WhiteSpace(UiWhiteSpace.Normal),
                Ui.Text(state.Phase).FontSize(12f).Bold().Color("#8AD7FF").WhiteSpace(UiWhiteSpace.Normal),
                Ui.Row(
                    Btn("Nudge hero", "nudge", r => r.NudgeHero()),
                    Btn("Checkpoint", "checkpoint", r => r.Checkpoint()),
                    Btn("Disconnect", "disconnect", r => r.Disconnect()),
                    Btn("Reconnect", "reconnect", r => r.Reconnect()),
                    Btn("Inject missing frame", "missing", r => r.RejectMissingFrame()),
                    Btn("Inject duplicate frame", "dup", r => r.RejectDuplicateFrame()),
                    Btn("Inject stale frame", "stale", r => r.RejectStaleFrame())).Gap(6f).Wrap(),
                Section("Timelines", new[]
                {
                    $"authority  tick {state.AuthorityTick}",
                    $"client     tick {state.ClientTick}",
                    gapLine,
                    $"recovery source: {state.RecoverySource}   checkpoint digest {state.CheckpointDigest}",
                }, state.Disconnected ? "#FF8A8A" : "#8DE3AE"),
                Section("Fault injections", new[]
                {
                    "missing / duplicate / stale frames are validated on ingest and rejected with the exact reason — no silent healing",
                }, "#F0C36B"),
                Section("Trace", state.LogLines, "#FFB38A"))
            .Width(560f).Padding(14f).Gap(8f).Radius(8f).Background("#0B1520").Border(1f, Color("#2F475E")))
            .WidthPercent(100f).HeightPercent(100f).Padding(16f).Align(UiAlignItems.Start).ZIndex(55);
    }

    private UiElementBuilder Btn(string label, string id, Action<ReconnectRecoveryRuntime> action)
        => Ui.Button(label, _ => action(_runtime)).Id($"reconnect-{id}").Height(30f);

    private static UiElementBuilder Section(string title, IReadOnlyList<string> lines, string accent)
    {
        var children = new List<UiElementBuilder> { Ui.Text(title).FontSize(12f).Bold().Color(accent) };
        for (int i = 0; i < lines.Count; i++) children.Add(Ui.Text(lines[i]).FontSize(11f).Color(i == 0 ? "#F5F7FA" : "#C7D0DD").WhiteSpace(UiWhiteSpace.Normal));
        return Ui.Column(children.ToArray()).Width(520f).Padding(10f).Gap(5f).Background("#0E1823").Border(1f, Color("#284154"));
    }

    private static UiColor Color(string hex) => UiColor.TryParse(hex, out UiColor color) ? color : throw new InvalidOperationException($"Unsupported color '{hex}'.");
}

public sealed class ReconnectRecoveryShowcaseModEntry : IMod
{
    public void OnLoad(IModContext context)
    {
        var runtimeHolder = new ReconnectRecoveryRuntime?[] { null };
        context.OnEvent(GameEvents.GameStart, ctx =>
        {
            if (ctx.Get(CoreServiceKeys.Engine) is not GameEngine engine ||
                (engine.GlobalContext.TryGetValue(ReconnectRecoveryShowcaseIds.InstalledKey, out object? value) && value is true))
            {
                return Task.CompletedTask;
            }

            engine.GlobalContext[ReconnectRecoveryShowcaseIds.InstalledKey] = true;
            var runtime = runtimeHolder[0] ??= new ReconnectRecoveryRuntime(engine);
            engine.GlobalContext[ReconnectRecoveryShowcaseIds.RuntimeKey] = runtime;
            engine.RegisterSystem(new ReconnectTickSystem(engine, runtime), SystemGroup.InputCollection);
            engine.RegisterPresentationSystem(new ReconnectPresentationSystem(engine, runtime));
            context.Log("[ReconnectRecoveryShowcaseMod] Reconnect recovery showcase installed.");
            return Task.CompletedTask;
        });
    }

    public void OnUnload()
    {
    }
}
