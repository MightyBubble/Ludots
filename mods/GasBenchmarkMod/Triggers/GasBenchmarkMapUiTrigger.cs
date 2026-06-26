using System;
using System.Threading.Tasks;
using Ludots.Core.Engine;
using Ludots.Core.Map;
using Ludots.Core.Scripting;
using Ludots.UI;
using Ludots.UI.Compose;
using Ludots.UI.Runtime;
using Ludots.UI.Runtime.Actions;
using Ludots.UI.Surface;

namespace GasBenchmarkMod.Triggers
{
    public sealed class GasBenchmarkMapUiTrigger : Trigger
    {
        public GasBenchmarkMapUiTrigger()
        {
            EventKey = GameEvents.MapLoaded;
            AddCondition(ctx => ctx.IsMap(GasBenchmarkMapIds.GasBenchmark));
        }

        public override Task ExecuteAsync(ScriptContext context)
        {
            Console.WriteLine("[GasBenchmarkMod] MapLoaded: gas_benchmark (mounting UI)...");

            var engine = context.GetEngine();
            if (engine == null)
            {
                return Task.CompletedTask;
            }

            if (context.Get(CoreServiceKeys.UiSurfaceHost) is not IUiSurfaceHost surfaceHost)
            {
                Console.WriteLine("[GasBenchmarkMod] UiSurfaceHost missing in ScriptContext.");
                return Task.CompletedTask;
            }

            UiSurfaceLeaseHandle lease = surfaceHost.Acquire(new UiSurfaceLeaseRequest(
                "GasBenchmark.MapMenu",
                UiSurfaceSegment.Main,
                priority: 10,
                exclusive: true));
            surfaceHost.Publish(
                lease,
                UiSurfaceContribution.FromBuilder(() => BuildRoot(
                    () =>
                    {
                        Console.WriteLine("[GasBenchmarkMod] UI click: Run GAS Benchmark");
                        engine.TriggerManager.FireEvent(GasBenchmarkEvents.RunGasBenchmark, engine.CreateContext());
                    },
                    () => engine.LoadMap(new MapId(engine.MergedConfig.StartupMapId)))));
            Console.WriteLine("[GasBenchmarkMod] UI mounted for gas_benchmark.");
            return Task.CompletedTask;
        }

        private static UiElementBuilder BuildRoot(Action runBenchmark, Action backToEntry)
        {
            return Ui.Column(
                    Ui.Text("GAS BENCHMARK")
                        .FontSize(54f)
                        .Bold()
                        .Color(UiColor.White),
                    Ui.Text("Click to spawn 100000 entities and run GAS benchmark.")
                        .FontSize(20f)
                        .Color(UiColor.LightGray),
                    Ui.Row(
                            BuildButton("Run GAS Benchmark", UiColor.Gold, UiColor.Black, _ => runBenchmark()),
                            BuildButton("Back to Entry", UiColor.DimGray, UiColor.White, _ => backToEntry()))
                        .Gap(12f)
                        .Wrap())
                .WidthPercent(100f)
                .HeightPercent(100f)
                .Justify(UiJustifyContent.Center)
                .Align(UiAlignItems.Center)
                .Background(new UiColor(0, 0, 0, 200))
                .Gap(18f);
        }

        private static UiElementBuilder BuildButton(string text, UiColor background, UiColor foreground, Action<UiActionContext> onClick)
        {
            return Ui.Button(text, onClick)
                .FontSize(24f)
                .Padding(18f, 14f)
                .Radius(10f)
                .Background(background)
                .Color(foreground);
        }
    }
}
