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

namespace PerformanceMod.Triggers
{
    public sealed class EntryBenchmarkMenuTrigger : Trigger
    {
        public EntryBenchmarkMenuTrigger()
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
                return Task.CompletedTask;
            }

            UiSurfaceLeaseHandle lease = surfaceHost.Acquire(new UiSurfaceLeaseRequest(
                "Performance.EntryMenu",
                UiSurfaceSegment.Main,
                priority: 10,
                exclusive: true));
            surfaceHost.Publish(
                lease,
                UiSurfaceContribution.FromBuilder(() => BuildRoot(
                    () => engine.LoadMap(PerformanceMapIds.Benchmark),
                    () => engine.LoadMap(new MapId(engine.MergedConfig.StartupMapId)))));
            return Task.CompletedTask;
        }

        private static UiElementBuilder BuildRoot(System.Action goBenchmark, System.Action goEntry)
        {
            return Ui.Column(
                    Ui.Text("PERFORMANCE")
                        .FontSize(54f)
                        .Bold()
                        .Color(UiColor.White),
                    Ui.Text("Entry menu: open benchmark map from here.")
                        .FontSize(20f)
                        .Color(UiColor.LightGray)
                        .Margin(0f, 12f),
                    BuildButton("Open Benchmark Map", UiColor.Gold, UiColor.Black, _ => goBenchmark()),
                    BuildButton("Back to Entry", UiColor.DimGray, UiColor.White, _ => goEntry()))
                .WidthPercent(100f)
                .HeightPercent(100f)
                .Justify(UiJustifyContent.Center)
                .Align(UiAlignItems.Center)
                .Background(new UiColor(0, 0, 0, 200))
                .Gap(16f);
        }

        private static UiElementBuilder BuildButton(string text, UiColor background, UiColor foreground, System.Action<UiActionContext> onClick)
        {
            return Ui.Button(text, onClick)
                .FontSize(24f)
                .Padding(18f, 14f)
                .Radius(10f)
                .Background(background)
                .Color(foreground)
                .Width(260f);
        }
    }
}
