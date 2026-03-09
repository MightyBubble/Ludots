using System.Threading.Tasks;
using Ludots.Core.Config;
using Ludots.Core.Engine;
using Ludots.Core.Map;
using Ludots.Core.Scripting;
using Ludots.UI;
using Ludots.UI.Compose;
using Ludots.UI.Runtime;

namespace PerformanceMod.Triggers
{
    public class EntryBenchmarkMenuTrigger : Trigger
    {
        public EntryBenchmarkMenuTrigger()
        {
            EventKey = GameEvents.MapLoaded;
            AddCondition(ctx =>
            {
                var engine = ctx.GetEngine();
                if (engine?.MergedConfig == null) return false;
                return ctx.IsMap(new MapId(engine.MergedConfig.StartupMapId));
            });
        }

        public override Task ExecuteAsync(ScriptContext context)
        {
            GameEngine? engine = context.GetEngine();
            UIRoot? uiRoot = context.Get(CoreServiceKeys.UIRoot) as UIRoot;
            if (engine == null || uiRoot == null) return Task.CompletedTask;

            UiScene scene = UiSceneComposer.Compose(
                Ui.Column(
                        Ui.Text("PERFORMANCE").FontSize(54).Bold(),
                        Ui.Text("Entry menu: open benchmark map from here.").FontSize(20).Color("#D3D3D3"),
                        Ui.Button("Open Benchmark Map", _ => engine.LoadMap(PerformanceMapIds.Benchmark)).FontSize(28),
                        Ui.Button("Back to Entry", _ => engine.LoadMap(new MapId(engine.MergedConfig.StartupMapId))).FontSize(22).Background("#696969"))
                    .Width(1280)
                    .Height(720)
                    .Gap(16)
                    .Justify(UiJustifyContent.Center)
                    .Align(UiAlignItems.Center)
                    .Background(new SkiaSharp.SKColor(0, 0, 0, 200)));

            uiRoot.MountScene(scene);
            uiRoot.IsDirty = true;
            return Task.CompletedTask;
        }
    }
}
