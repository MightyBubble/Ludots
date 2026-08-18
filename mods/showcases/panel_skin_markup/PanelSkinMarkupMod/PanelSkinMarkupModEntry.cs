using System;
using System.Threading.Tasks;
using Arch.Core;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using Ludots.Core.UI.PanelHosting;
using Ludots.Core.UI.PanelProjection;
using Ludots.UI.Compose;
using Ludots.UI.Runtime;
using Ludots.UI.Surface;

namespace PanelSkinMarkupMod;

public sealed class PanelSkinMarkupModEntry : IMod
{
    public void OnLoad(IModContext context)
    {
        context.Log("[PanelSkinMarkupMod] Loaded");
        context.OnEvent(GameEvents.MapLoaded, ctx =>
        {
            if (ctx.Get(CoreServiceKeys.Engine) is not GameEngine engine ||
                engine.GetService(CoreServiceKeys.PanelHost) is not PanelHost panelHost ||
                ctx.Get(CoreServiceKeys.UiSurfaceHost) is not IUiSurfaceHost surfaceHost)
            {
                return Task.CompletedTask;
            }

            Entity hero = Entity.Null;
            var query = new QueryDescription().WithAll<Team>();
            engine.World.Query(in query, (Entity e, ref Team team) =>
            {
                if (team.Id == 1 && hero == Entity.Null) hero = e;
            });
            if (hero == Entity.Null) return Task.CompletedTask;

            PanelInstanceHandle handle = panelHost.Instantiate("panel.fireball.status", "markup-hero", hero);
            var lease = surfaceHost.Acquire(new UiSurfaceLeaseRequest("markup-panel", UiSurfaceSegment.Main, priority: 100));

            // Contribution: builder reads PanelHost live values each rebuild.
            surfaceHost.Publish(lease, UiSurfaceContribution.FromBuilder(
                _ => PanelShowcaseShared.BuildPanel(panelHost, handle, "Markup", 68, 136, 204)));

            // System: invalidate every frame so the builder re-reads realtime values.
            engine.RegisterPresentationSystem(new PanelInvalidationSystem(surfaceHost, lease));
            return Task.CompletedTask;
        });
    }

    public void OnUnload() { }
}

/// <summary>
/// Invalidates the UI surface lease each frame so the contribution's buildRoot
/// lambda re-executes with fresh PanelHost values (realtime path).
/// </summary>
public sealed class PanelInvalidationSystem : Arch.System.ISystem<float>
{
    private readonly IUiSurfaceHost _host;
    private readonly UiSurfaceLeaseHandle _lease;

    public PanelInvalidationSystem(IUiSurfaceHost host, UiSurfaceLeaseHandle lease)
    {
        _host = host;
        _lease = lease;
    }

    public void Initialize() { }
    public void Update(in float dt) => _host.Invalidate(_lease);
    public void BeforeUpdate(in float dt) { }
    public void AfterUpdate(in float dt) { }
    public void Dispose() { }
}

/// <summary>
/// Shared panel builder used by all four skin mods — each passes its own accent color.
/// </summary>
public static class PanelShowcaseShared
{
    public static UiElementBuilder BuildPanel(PanelHost host, PanelInstanceHandle handle, string skinLabel, byte accentR, byte accentG, byte accentB)
    {
        float hp = 0, mp = 0, atk = 0;
        if (host.TryGetValues(handle, out PanelVariableSet? v) && v != null)
        {
            hp = v.Get("health"); mp = v.Get("mana"); atk = v.Get("attack");
        }
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
                new UiElementBuilder(UiNodeKind.Text).Text("🔥 FIREBALL STATUS").FontSize(16).Bold().Color(new UiColor(255, 102, 0)),
                new UiElementBuilder(UiNodeKind.Text).Text($"HP  {hp:F0} / 100").FontSize(14).Color(new UiColor(255, 68, 68)),
                new UiElementBuilder(UiNodeKind.Text).Text($"MP  {mp:F0} / 80").FontSize(14).Color(new UiColor(68, 136, 255)),
                new UiElementBuilder(UiNodeKind.Text).Text($"ATK {atk:F0}").FontSize(14).Color(new UiColor(255, 170, 0)),
                new UiElementBuilder(UiNodeKind.Text).Text($"[{skinLabel}] Press Q").FontSize(11).Color(dim)
            );
    }
}
