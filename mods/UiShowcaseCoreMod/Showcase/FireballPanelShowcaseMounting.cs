using System;
using Arch.Core;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Scripting;
using Ludots.Core.UI.PanelHosting;
using Ludots.Core.UI.PanelProjection;
using Ludots.UI.Compose;
using Ludots.UI.Runtime;
using Ludots.UI.Surface;

namespace UiShowcaseCoreMod.Showcase;

public static class FireballPanelShowcaseMounting
{
    public const string PanelTemplateId = "panel.fireball.status";
    public const string PanelAnchor = "screen.topRight";

    public static void InstallSkinSurface(
        ScriptContext context,
        string ownerId,
        string skinLabel,
        byte accentR,
        byte accentG,
        byte accentB)
    {
        GameEngine engine = context.Get(CoreServiceKeys.Engine)
            ?? throw new InvalidOperationException("Fireball panel skin requires GameEngine in ScriptContext.");
        PanelHost panelHost = engine.GetService(CoreServiceKeys.PanelHost)
            ?? throw new InvalidOperationException("Fireball panel skin requires PanelHost.");
        IUiSurfaceHost surfaceHost = context.Get(CoreServiceKeys.UiSurfaceHost) as IUiSurfaceHost
            ?? throw new InvalidOperationException("Fireball panel skin requires UiSurfaceHost.");

        engine.RegisterPresentationSystem(new FireballPanelSurfaceSystem(
            panelHost,
            surfaceHost,
            ownerId,
            skinLabel,
            accentR,
            accentG,
            accentB));
    }

    public static PanelInstanceHandle RequireSinglePanelInstance(PanelHost host)
    {
        if (host == null)
        {
            throw new ArgumentNullException(nameof(host));
        }

        PanelInstanceHandle found = default;
        int count = 0;
        foreach (PanelHostInstanceInfo info in host.SnapshotInstances())
        {
            if (!string.Equals(info.TemplateId, PanelTemplateId, StringComparison.Ordinal) ||
                !string.Equals(info.Anchor, PanelAnchor, StringComparison.Ordinal))
            {
                continue;
            }

            found = info.Handle;
            count++;
        }

        return count switch
        {
            1 => found,
            0 => throw new InvalidOperationException(
                $"Fireball panel skin requires an existing '{PanelTemplateId}' instance at '{PanelAnchor}'. The map trigger must create it before the first presentation frame."),
            _ => throw new InvalidOperationException(
                $"Fireball panel skin expected one '{PanelTemplateId}' instance at '{PanelAnchor}', but found {count}.")
        };
    }

    public static UiElementBuilder BuildPanel(
        PanelHost host,
        PanelInstanceHandle handle,
        string skinLabel,
        byte accentR,
        byte accentG,
        byte accentB)
    {
        if (!host.TryGetValues(handle, out PanelVariableSet values))
        {
            throw new InvalidOperationException(
                $"Fireball panel skin cannot read stale panel handle {handle.Id}#{handle.Generation}.");
        }

        float hp = values.Get("health");
        float mp = values.Get("mana");
        float atk = values.Get("attack");
        float hpBase = values.Get("healthBase");
        float mpBase = values.Get("manaBase");
        var accent = new UiColor(accentR, accentG, accentB);
        var dim = new UiColor(136, 136, 136);

        return new UiElementBuilder(UiNodeKind.Container).Column()
            .Background(new UiColor(20, 20, 35, 220))
            .Border(2, accent)
            .Radius(8)
            .Padding(12)
            .Width(260)
            .Gap(4)
            .Children(
                new UiElementBuilder(UiNodeKind.Text).Text("FIREBALL STATUS").FontSize(16).Bold().Color(new UiColor(255, 102, 0)),
                new UiElementBuilder(UiNodeKind.Text).Text($"HP  {hp:F0} / {hpBase:F0}").FontSize(14).Color(new UiColor(255, 68, 68)),
                new UiElementBuilder(UiNodeKind.Text).Text($"MP  {mp:F0} / {mpBase:F0}").FontSize(14).Color(new UiColor(68, 136, 255)),
                new UiElementBuilder(UiNodeKind.Text).Text($"ATK {atk:F0}").FontSize(14).Color(new UiColor(255, 170, 0)),
                new UiElementBuilder(UiNodeKind.Text).Text($"[{skinLabel}] Press Q").FontSize(11).Color(dim));
    }

    private sealed class FireballPanelSurfaceSystem : ISystem<float>
    {
        private readonly PanelHost _panelHost;
        private readonly IUiSurfaceHost _surfaceHost;
        private readonly string _ownerId;
        private readonly string _skinLabel;
        private readonly byte _accentR;
        private readonly byte _accentG;
        private readonly byte _accentB;
        private UiSurfaceLeaseHandle _lease;
        private PanelInstanceHandle _panel;
        private bool _mounted;

        public FireballPanelSurfaceSystem(
            PanelHost panelHost,
            IUiSurfaceHost surfaceHost,
            string ownerId,
            string skinLabel,
            byte accentR,
            byte accentG,
            byte accentB)
        {
            _panelHost = panelHost ?? throw new ArgumentNullException(nameof(panelHost));
            _surfaceHost = surfaceHost ?? throw new ArgumentNullException(nameof(surfaceHost));
            _ownerId = string.IsNullOrWhiteSpace(ownerId)
                ? throw new ArgumentException("Fireball panel surface owner id is required.", nameof(ownerId))
                : ownerId.Trim();
            _skinLabel = string.IsNullOrWhiteSpace(skinLabel)
                ? throw new ArgumentException("Fireball panel skin label is required.", nameof(skinLabel))
                : skinLabel.Trim();
            _accentR = accentR;
            _accentG = accentG;
            _accentB = accentB;
        }

        public void Initialize() { }

        public void Update(in float dt)
        {
            if (!_mounted)
            {
                _panel = RequireSinglePanelInstance(_panelHost);
                _lease = _surfaceHost.Acquire(new UiSurfaceLeaseRequest(_ownerId, UiSurfaceSegment.Main, priority: 100));
                _surfaceHost.Publish(_lease, UiSurfaceContribution.FromBuilder(
                    _ => BuildPanel(_panelHost, _panel, _skinLabel, _accentR, _accentG, _accentB)));
                _mounted = true;
                Ludots.Core.Diagnostics.Log.Info(
                    in Ludots.Core.Diagnostics.LogChannels.Engine,
                    $"[FireballSkin] surface '{_skinLabel}' MOUNTED panel {_panel.Id}#{_panel.Generation}.");
                return;
            }

            _surfaceHost.Invalidate(_lease);
        }

        public void BeforeUpdate(in float dt) { }
        public void AfterUpdate(in float dt) { }
        public void Dispose() { }
    }
}
