using System;
using System.Threading.Tasks;
using Ludots.Core.Engine;
using Ludots.Core.Scripting;
using Ludots.UI;
using Ludots.UI.Compose;
using Ludots.UI.Runtime;

namespace GasBenchmarkMod.Triggers
{
    public class GasBenchmarkMapUiTrigger : Trigger
    {
        public GasBenchmarkMapUiTrigger()
        {
            EventKey = GameEvents.MapLoaded;
            AddCondition(ctx => ctx.IsMap(GasBenchmarkMapIds.GasBenchmark));
        }

        public override Task ExecuteAsync(ScriptContext context)
        {
            Console.WriteLine("[GasBenchmarkMod] MapLoaded: gas_benchmark (mounting runtime UI)...");

            var engine = context.GetEngine();
            var uiRoot = context.Get(CoreServiceKeys.UIRoot) as UIRoot;
            if (engine == null || uiRoot == null)
            {
                Console.WriteLine("[GasBenchmarkMod] UIRoot missing in ScriptContext.");
                return Task.CompletedTask;
            }

            UiScene scene = UiSceneComposer.Compose(
                Ui.Column(
                        Ui.Text("GAS BENCHMARK").FontSize(54).Bold(),
                        Ui.Text("Click to spawn 100000 entities and run GAS benchmark.").FontSize(20).Color("#D3D3D3"),
                        Ui.Button("Run GAS Benchmark", _ =>
                        {
                            Console.WriteLine("[GasBenchmarkMod] UI click: Run GAS Benchmark");
                            engine.TriggerManager.FireEvent(GasBenchmarkEvents.RunGasBenchmark, engine.CreateContext());
                        }).FontSize(28),
                        Ui.Button("Back to Entry", _ => engine.LoadMap(new Ludots.Core.Map.MapId(engine.MergedConfig.StartupMapId))).FontSize(22).Background("#696969"))
                    .Width(1280)
                    .Height(720)
                    .Gap(16)
                    .Justify(UiJustifyContent.Center)
                    .Align(UiAlignItems.Center)
                    .Background(new SkiaSharp.SKColor(0, 0, 0, 200)));

            uiRoot.MountScene(scene);
            uiRoot.IsDirty = true;
            Console.WriteLine("[GasBenchmarkMod] UI mounted for gas_benchmark.");
            return Task.CompletedTask;
        }
    }
}
