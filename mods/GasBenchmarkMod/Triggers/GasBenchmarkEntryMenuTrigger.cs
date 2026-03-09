using System;
using System.Threading.Tasks;
using Ludots.Core.Config;
using Ludots.Core.Engine;
using Ludots.Core.Map;
using Ludots.Core.Scripting;
using Ludots.UI;
using Ludots.UI.Compose;
using Ludots.UI.Runtime;

namespace GasBenchmarkMod.Triggers
{
    public class GasBenchmarkEntryMenuTrigger : Trigger
    {
        public GasBenchmarkEntryMenuTrigger()
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
            if (engine == null || uiRoot == null)
            {
                Console.WriteLine("[GasBenchmarkMod] Missing engine or UIRoot in ScriptContext (entry).");
                return Task.CompletedTask;
            }

            UiScene scene = UiSceneComposer.Compose(
                Ui.Column(
                        Ui.Text("GAS BENCHMARK").FontSize(54).Bold(),
                        Ui.Text("Entry menu: open GAS benchmark map from here.").FontSize(20).Color("#D3D3D3"),
                        Ui.Button("Open GAS Benchmark Map", _ => engine.LoadMap(GasBenchmarkMapIds.GasBenchmark)).FontSize(28))
                    .Width(1280)
                    .Height(720)
                    .Gap(16)
                    .Justify(UiJustifyContent.Center)
                    .Align(UiAlignItems.Center)
                    .Background(new SkiaSharp.SKColor(0, 0, 0, 200)));

            uiRoot.MountScene(scene);
            uiRoot.IsDirty = true;
            Console.WriteLine("[GasBenchmarkMod] Entry menu mounted using runtime scene.");
            return Task.CompletedTask;
        }
    }
}
