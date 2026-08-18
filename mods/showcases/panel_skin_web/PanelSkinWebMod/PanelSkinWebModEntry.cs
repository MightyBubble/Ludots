using System;
using System.Threading.Tasks;
using Arch.Core;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using Ludots.Core.UI.PanelHosting;
using Ludots.UI.Compose;
using Ludots.UI.Runtime;
using Ludots.UI.Surface;
using PanelSkinMarkupMod;

namespace PanelSkinWebMod;

public sealed class PanelSkinWebModEntry : IMod
{
    public void OnLoad(IModContext context)
    {
        context.Log("[PanelSkinWebMod] Loaded — Web UI skin");
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

            PanelInstanceHandle handle = panelHost.Instantiate("panel.fireball.status", "web-hero", hero);
            var lease = surfaceHost.Acquire(new UiSurfaceLeaseRequest("web-panel", UiSurfaceSegment.Main, priority: 100));

            surfaceHost.Publish(lease, UiSurfaceContribution.FromBuilder(
                _ => PanelShowcaseShared.BuildPanel(panelHost, handle, "Web UI", 255, 152, 0)));

            engine.RegisterPresentationSystem(new PanelInvalidationSystem(surfaceHost, lease));
            return Task.CompletedTask;
        });
    }

    public void OnUnload() { }
}
