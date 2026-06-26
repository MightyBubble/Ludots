using System;
using System.Threading.Tasks;
using Ludots.Core.Config;
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
    public sealed class GasBenchmarkEntryMenuTrigger : Trigger
    {
        public GasBenchmarkEntryMenuTrigger()
        {
            EventKey = GameEvents.MapLoaded;
            AddCondition(ctx =>
            {
                var engine = ctx.GetEngine();
                return engine?.MergedConfig != null && ctx.IsMap(new MapId(engine.MergedConfig.StartupMapId));
            });
        }

        public override Task ExecuteAsync(ScriptContext context)
        {
            var engine = context.GetEngine();
            if (engine == null)
            {
                return Task.CompletedTask;
            }

            if (context.Get(CoreServiceKeys.UiSurfaceHost) is not IUiSurfaceHost surfaceHost)
            {
                Console.WriteLine("[GasBenchmarkMod] UiSurfaceHost missing in ScriptContext (entry).");
                return Task.CompletedTask;
            }

            UiSurfaceLeaseHandle lease = surfaceHost.Acquire(new UiSurfaceLeaseRequest(
                "GasBenchmark.EntryMenu",
                UiSurfaceSegment.Main,
                priority: 10,
                exclusive: true));
            surfaceHost.Publish(
                lease,
                UiSurfaceContribution.FromBuilder(() => BuildRoot(() => engine.LoadMap(GasBenchmarkMapIds.GasBenchmark))));
            Console.WriteLine("[GasBenchmarkMod] Entry menu mounted.");
            return Task.CompletedTask;
        }

        private static UiElementBuilder BuildRoot(Action openGasBenchmark)
        {
            return Ui.Column(
                    Ui.Text("GAS BENCHMARK")
                        .FontSize(54f)
                        .Bold()
                        .Color(UiColor.White),
                    Ui.Text("Entry menu: open GAS benchmark map from here.")
                        .FontSize(20f)
                        .Color(UiColor.LightGray),
                    BuildButton("Open GAS Benchmark Map", UiColor.Gold, UiColor.Black, _ => openGasBenchmark()))
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
                .FontSize(28f)
                .Padding(18f, 14f)
                .Radius(10f)
                .Background(background)
                .Color(foreground);
        }
    }
}
