using System.Threading.Tasks;
using Ludots.Core.Config;
using Ludots.Core.Engine;
using Ludots.Core.Map;
using Ludots.Core.Scripting;
using Ludots.UI;
using Ludots.UI.Compose;
using Ludots.UI.Runtime;

namespace PerformanceVisualizationMod.Triggers
{
    public class VisualBenchmarkEntryMenuTrigger : Trigger
    {
        public VisualBenchmarkEntryMenuTrigger()
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
                        Ui.Text("Ludots Visual Benchmark").FontSize(48).Bold(),
                        Ui.Button("Start Visual Benchmark", _ => engine.LoadMap(VisualBenchmarkMapIds.VisualBenchmark)).FontSize(24).Background("#FFFFFF").Color("#000000"))
                    .Width(1280)
                    .Height(720)
                    .Gap(40)
                    .Justify(UiJustifyContent.Center)
                    .Align(UiAlignItems.Center)
                    .Background("#000000"));

            uiRoot.MountScene(scene);
            uiRoot.IsDirty = true;
            return Task.CompletedTask;
        }
    }
}
